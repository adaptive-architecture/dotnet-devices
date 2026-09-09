# Printing Expansion Design

Date: 2026-09-09
Status: Approved for planning

## 1. Purpose

`AdaptArch.Devices` models printers, but it prints only one way: raw bytes to TCP
port 9100. Four capabilities are absent:

1. A transport for the operating system spooler.
2. A producer for `PrinterConfiguration`. No code fills it today.
3. Print job submission over IPP.
4. Job queue reads and job progress monitoring.

`IPrinter` has no implementation. Because of this, `PrintOptions`, `PrintJobInfo`,
`PrinterConfiguration`, `DuplexMode`, `PrintColorMode` and `PrintOrientation` are
public types that no code reads or writes. This design adds the four capabilities
and makes `IPrinter` real, which gives those types a consumer.

## 2. Constraints

- The core package keeps zero runtime dependencies for the dependency injection
  code. Registrations stay in `AdaptArch.Devices.DependencyInjection`.
- `src/` sets `IsAotCompatible` and treats warnings as errors. All new code must
  be trim-safe and native-AOT-safe.
- No new NuGet dependency. `SharpIppNext`, which is already approved, supplies
  every IPP operation this design needs.
- One library with runtime operating system checks, as `docs/architecture.md`
  requires. No new platform-specific project.
- Every `await` calls `.ConfigureAwait(false)`. RCS1090 is an error.

## 3. Key decision: CUPS is an IPP server

CUPS listens for IPP on `localhost:631`. Linux and macOS therefore need no native
interop for the spooler. The spooler driver for these platforms sends IPP to the
local daemon and gets submission, the job queue, the configuration and the status
from the IPP code that this design already builds.

`SharpIppNext` supplies the necessary operations: `PrintJobAsync`, `GetJobsAsync`,
`GetJobAttributesAsync`, `CancelJobAsync`, `GetPrinterAttributesAsync` and the CUPS
extension `GetCUPSPrintersAsync`.

Two alternatives were rejected:

- **`libcups` P/Invoke** — it adds a third native surface, and macOS deprecates
  parts of the library.
- **The `lp` and `lpstat` commands** — they need text parsing, and they give no
  job identifier that the code can trust.

Windows has no local IPP server. Windows is therefore the only platform that needs
native interop.

## 4. Architecture

### 4.1 Spooler driver

An internal interface gives one shape to the two platform drivers:

```csharp
internal interface ISpoolerDriver
{
    Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken);
    Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken);
    Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken);
    Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken);
    Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken);
    Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
    Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
}
```

| Platform | Implementation | Mechanism |
| --- | --- | --- |
| Linux, macOS | `CupsSpoolerDriver` | IPP to `ipp://localhost:631/printers/<queue>`. `CUPS-Get-Printers` enumerates the queues. |
| Windows | `WindowsSpoolerDriver` | `LibraryImport` bindings to `winspool.drv`. |

A factory selects the driver with `OperatingSystem.IsWindows()`. A driver that runs
on the wrong platform throws `PlatformNotSupportedException`.

The Windows driver uses these entry points:

- `EnumPrinters` — enumerate the queues.
- `OpenPrinter`, `StartDocPrinter`, `StartPagePrinter`, `WritePrinter`,
  `EndPagePrinter`, `EndDocPrinter`, `ClosePrinter` — submit a payload.
- `EnumJobs`, `GetJob` — read the queue.
- `SetJob` — cancel a job.
- `DeviceCapabilities` — read the configuration.
- `GetPrinter` — read the status.

`LibraryImport` is a source generator. It emits no reflection, so trim and AOT stay
clean. All structures use `[StructLayout]` with explicit character sets.

The payload goes to the spooler with the `RAW` data type, because the library sends
printer languages such as ZPL and ESC/POS. A payload whose content type is
`application/pdf` or `image/png` uses the content type as the spooler data type on
CUPS, and stays `RAW` on Windows.

### 4.2 IPP core, refactored

`IppPrinterStatusClient` holds three things inside one read-only method: the scheme
and path probe, the client construction, and the exception mapping. Printing, the
configuration read and the job queries all need the same three things.

Two internal types take that responsibility:

- `IppEndpointResolver` — finds the working printer URI one time and keeps it for
  the life of the instance. It tries the caller path, then `/ipp/print`, then
  `/ipp/port1`, over `ipps` and then `ipp`.
- `IppOperations` — builds the requests, sends them, and maps the exceptions.

`IppPrinterStatusClient` keeps its public behaviour and its public signature. It
calls the new internal types. This is refactoring in support of the task.

### 4.3 IPrinter implementations

| Type | Endpoint | Print | Status | Configuration | Jobs |
| --- | --- | --- | --- | --- | --- |
| `IppPrinter` | `NetworkPrinterEndpoint` | IPP `Print-Job` | IPP `Get-Printer-Attributes` | IPP `Get-Printer-Attributes` | IPP `Get-Jobs` |
| `SpoolerPrinter` | `SpoolerPrinterEndpoint` | `ISpoolerDriver` | `ISpoolerDriver` | `ISpoolerDriver` | `ISpoolerDriver` |
| `RawPrinter` | `NetworkPrinterEndpoint` | `TcpPrinterTransport` | SNMP, then IPP | Empty | None |

`RawPrinter` serves a printer that offers only the port 9100 channel. It returns a
completed `PrintJobInfo` with a generated identifier, because the raw channel gives
no job identifier. `GetJobsAsync` on such a printer returns an empty list.

`IPrinter` itself has no job methods. The "Jobs" column names the source that backs
this printer inside `CompositePrintJobQueue` and inside the monitor.

An empty `PrinterConfiguration` means "not known", not "not supported". The
documentation must say this.

### 4.4 Printer factory

```csharp
public interface IPrinterFactory
{
    IPrinter Open(DiscoveredPrinter printer);
    Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken);
}
```

`Open` selects the implementation from the endpoint type. For a
`NetworkPrinterEndpoint` on port 631 it makes an `IppPrinter`. For a
`NetworkPrinterEndpoint` on port 9100 it makes a `RawPrinter`. For a
`SpoolerPrinterEndpoint` it makes a `SpoolerPrinter`. For a `UsbPrinterEndpoint` it
throws `NotSupportedException`, because no USB transport exists.

`OpenAsync` accepts a `PrinterId` with no endpoint. For `PrinterIdKind.Network` it
probes IPP first, then falls back to the raw channel. For `PrinterIdKind.Spooler` it
makes a `SpoolerPrinter` for the named queue.

### 4.5 Job progress monitor

```csharp
public interface IPrintJobMonitor
{
    IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId printerId,
        string jobId,
        PrintJobMonitorOptions options,
        CancellationToken cancellationToken);
}
```

The monitor polls `IPrintJobQueue.GetJobAsync` and yields one item for each change
of state or of impression count. It yields the first reading immediately. It stops
when the job reaches `Completed`, `Failed` or `Canceled`, when the timeout expires,
or when the caller cancels.

`PrintJobMonitorOptions`:

- `PollInterval` — the time between reads. The default is one second.
- `Timeout` — the maximum total time. The default is no limit.

A job that the source no longer reports counts as `Completed`, because both CUPS and
the Windows spooler remove a finished job from the queue after a short time. The
monitor yields a final `Completed` reading in that condition.

One implementation, `PollingPrintJobMonitor`, serves every printer type. It takes
an `IPrintJobQueue` and a `TimeProvider`. The tests supply a fake `TimeProvider`, so
no test waits.

## 5. Print options

`PrintOptions` gets one new property:

```csharp
public UnsupportedOptionBehavior OnUnsupported { get; set; } = UnsupportedOptionBehavior.Send;
```

```csharp
public enum UnsupportedOptionBehavior
{
    Send,
    Throw,
    Drop,
}
```

- `Send` — map the options and submit them. The printer applies its own rules. This
  costs no extra request.
- `Throw` — read the configuration first. Throw `NotSupportedException` that names
  the first option the printer does not support.
- `Drop` — read the configuration first. Remove the options the printer does not
  support, submit the job, and name the removed options in
  `PrintJobInfo.DroppedOptions`.

`Throw` and `Drop` use the configuration that the `IPrinter` instance holds. The
instance reads the configuration one time and keeps it.

A printer with an empty configuration cannot say which options it supports.
`Throw` and `Drop` therefore behave as `Send` on a `RawPrinter`. The documentation
must say this.

### 5.1 Option mapping

| `PrintOptions` | IPP attribute | Windows |
| --- | --- | --- |
| `Copies` | `copies` | `DEVMODE.dmCopies` |
| `Duplex` | `sides` | `DEVMODE.dmDuplex` |
| `ColorMode` | `print-color-mode` | `DEVMODE.dmColor` |
| `Orientation` | `orientation-requested` | `DEVMODE.dmOrientation` |
| `MediaSource` | `media-source` | `DEVMODE.dmDefaultSource` |
| `MediaSize` | `media` | `DEVMODE.dmPaperSize` |
| `ResolutionDpi` | `printer-resolution` | `DEVMODE.dmPrintQuality` |
| `JobName` | `job-name` | `DOC_INFO_1.pDocName` |

The Windows driver sends `RAW` data. A `DEVMODE` therefore applies only when the
queue has a driver that reads it. When the driver ignores `DEVMODE`, the behaviour
matches `Send`: the request goes out and the printer decides.

## 6. Model changes

`PrintJobInfo` gets four new properties. All are optional, so no existing code
breaks:

- `int? ImpressionsCompleted` — the pages printed. IPP:
  `job-impressions-completed`. Windows: `JOB_INFO_2.PagesPrinted`.
- `int? TotalImpressions` — the total pages, when the source reports it. IPP:
  `job-impressions`. Windows: `JOB_INFO_2.TotalPages`.
- `string? Detail` — a readable reason, from IPP `job-state-reasons` or from the
  Windows job status bits.
- `IReadOnlyList<string> DroppedOptions` — empty unless `Drop` removed an option.

`PrintJobState` needs no change. The IPP states map onto it:

| IPP `job-state` | `PrintJobState` |
| --- | --- |
| `pending` | `Queued` |
| `pending-held` | `Paused` |
| `processing` | `Printing` |
| `processing-stopped` | `Paused` |
| `completed` | `Completed` |
| `canceled` | `Canceled` |
| `aborted` | `Failed` |

## 7. Configuration producers

| Source | Attributes |
| --- | --- |
| IPP and CUPS | `printer-resolution-supported`, `sides-supported`, `color-supported`, `media-supported`, `media-default` |
| Windows | `DeviceCapabilities` with `DC_ENUMRESOLUTIONS`, `DC_DUPLEX`, `DC_COLORDEVICE`, `DC_PAPERNAMES` |
| Raw port 9100 | None. Empty configuration. |
| SNMP | None. SNMP is a status source, not a configuration source. |

## 8. Dependency injection

`AddPrinters()` adds these registrations:

```csharp
services.AddSingleton<IPrinterFactory, PrinterFactory>();
services.AddSingleton<IPrinterDiscovery, SpoolerPrinterDiscovery>();
services.AddSingleton<IPrintJobQueue, CompositePrintJobQueue>();
services.AddSingleton<IPrintJobMonitor, PollingPrintJobMonitor>();
services.AddSingleton(TimeProvider.System);
```

`CompositePrintJobQueue` sends a request to the IPP queue or to the spooler queue,
which it selects from `PrinterId.Kind`.

## 9. Errors

The current pattern continues:

| Condition | Exception |
| --- | --- |
| No channel answers | `InvalidOperationException` |
| A malformed reply | `InvalidDataException` |
| An endpoint a component cannot serve | `NotSupportedException` |
| A driver on the wrong operating system | `PlatformNotSupportedException` |
| An unsupported option, with `Throw` | `NotSupportedException` |
| A `winspool` call that fails | `InvalidOperationException` that carries the Win32 error code |

Cancellation always throws `OperationCanceledException`. A timeout inside a probe
does not: the probe moves to the next candidate.

## 10. Testing

Every driver sits behind an internal interface with a fake in the tests. This
matches the pattern that `IUdpChannel` and `IMdnsChannelFactory` already set.

- **IPP** — recorded request and response bytes, as
  `SnmpRequests`/`SnmpResponses` already do for SNMP. Tests cover the request
  builders, the response mappers, the state map and the exception map.
- **Windows interop** — tests cover the structure marshalling and the job mapping
  against a fake `ISpoolerDriver`. The `winspool` calls themselves are not unit
  tested, because they need the operating system.
- **Options** — tests cover all three values of `UnsupportedOptionBehavior`
  against a printer with a known configuration.
- **Monitor** — a scripted queue and a fake `TimeProvider`. No test waits.
- **Factory** — tests cover the choice of implementation for each endpoint type.

The sample gets a `--watch <host> <file>` command. It submits a job and prints the
progress until the job finishes.

## 11. Documentation

`docs/printers.md` and `docs/capabilities.md` need an update in the last phase. The
update must also correct the gaps that this work does not close:

- `PrinterContentTypes` also has `Png` and `Pdf`.
- `IppPrinterStatusClient` is `IDisposable`.
- The status clients have no interface, so a test cannot mock them.
- `DiscoveredPrinter` is not described.
- `TcpPrinterTransport` has a five second default connect timeout.
- The services are safe to share, which is why the container holds them as
  singletons.

## 12. Out of scope

- USB transport. The endpoint model stays, and the factory throws for it.
- SNMP version 3.
- IPP notifications with `Create-Printer-Subscriptions` and `Get-Notifications`.
  Polling serves every printer; few printers implement notifications.
- An LPD transport.
- Interfaces for the two status clients.

## 13. Delivery order

Each phase is complete on its own and can ship.

1. **IPP core and configuration** — `IppEndpointResolver`, `IppOperations`, the
   `IppPrinterStatusClient` refactor, and the IPP configuration reader.
2. **IPP printing** — `Print-Job`, the option mapping, `UnsupportedOptionBehavior`,
   `IppPrinter`, `IPrinterFactory`, `RawPrinter`.
3. **Job queue and monitor** — `IppPrintJobQueue`, the `PrintJobInfo` model change,
   `PollingPrintJobMonitor`, the sample `--watch` command.
4. **Spooler** — `ISpoolerDriver`, `CupsSpoolerDriver`, `WindowsSpoolerDriver`,
   `SpoolerPrinter`, `SpoolerPrinterDiscovery`, `SpoolerPrintJobQueue`, the
   dependency injection registrations, and the documentation update.
