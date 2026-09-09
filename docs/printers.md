# Printers

Cross-platform printer abstractions in `AdaptArch.Devices` (`AdaptArch.Devices.Printing` namespace).
The design separates *where* a printer is (endpoints) from *how* bytes get there (transports),
and models print data as raw byte streams annotated with a content type.

Core `AdaptArch.Devices` ships with zero runtime dependencies. Dependency injection
registrations live in the separate `AdaptArch.Devices.DependencyInjection` package.

## Identity and Endpoints

- `PrinterId` — stable identifier pairing a `PrinterIdKind` (`Spooler`, `Network`, `Usb`)
  with a value (queue name, host, or device path). Factories: `FromSpooler`, `FromNetwork`, `FromUsb`.
- `PrinterEndpoint` — pure-data description of reachability:
  - `NetworkPrinterEndpoint { Host, Port }` — TCP, defaults to the raw port 9100 channel.
  - `UsbPrinterEndpoint { VendorId, ProductId, SerialNumber? }` — USB descriptors.
  - `SpoolerPrinterEndpoint { Name }` — operating system print queue.
- `DiscoveredPrinter` — a printer found by discovery. It pairs `Id`, `Endpoint`, and `Info`.
  Every discovery API returns a list of these.

## Payloads

`PrinterPayload` carries `ReadOnlyMemory<byte> Data` plus a `ContentType` string so
transports and spoolers can route it correctly.

- `PrinterContentTypes` — constants for `Zpl`, `Epl`, `Cpcl`, `EscPos`, `Text`, `Png`, `Pdf`, `OctetStream`.
- `PrinterPayload.FromString(text, contentType, encoding?)` — for text-based languages such as ZPL.
- `PrinterPayload.FromBytes(data, contentType)` — for pre-encoded or binary streams.

```csharp
PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
```

## Printing

`IPrinter` is the main seam: `Id`, `Endpoint`, `Info`, plus `PrintAsync`,
`GetStatusAsync`, and `GetConfigurationAsync`. Mock it in unit tests instead of
touching hardware.

- `PrintAsync` returns `PrintJobInfo` (job id, `PrintJobState`, timestamps).
  Transports without job tracking return a completed job with a generated identifier.
- `PrintOptions` — optional per-job settings (copies, duplex, color mode, orientation,
  media source/size, resolution, job name). Unset properties fall back to printer defaults,
  which keeps jobs portable across spoolers with different capabilities.
- `PrinterStatus` — `PrinterStatusState` (`Idle`, `Processing`, `Paused`, `Error`,
  `Offline`, `Unknown`) plus `IsAcceptingJobs` and a human-readable `Detail`.
  Printers without a status channel report `Unknown`.
- `PrinterConfiguration` — supported DPIs, duplex/color support, media sizes. An empty
  configuration means the capabilities are **not known**, not that nothing is supported.
  A `RawPrinter`, for example, always reports an empty configuration, because the raw
  channel gives no way to ask a printer what it supports.

## Printing a job

`IPrinterFactory` turns a discovered printer, or a bare `PrinterId`, into a ready
`IPrinter` for the transport that fits it:

```csharp
IPrinterFactory factory = new PrinterFactory();
IPrinter printer = await factory.OpenAsync(PrinterId.FromNetwork("192.168.1.50"), cancellationToken).ConfigureAwait(false);
PrintJobInfo job = await printer.PrintAsync(payload, options: null, cancellationToken).ConfigureAwait(false);
```

`PrintOptions.OnUnsupported` says what to do with an option the printer does not
support:

- `Send` (the default) — send the option and let the printer decide. This costs no
  extra request.
- `Throw` — read the configuration first, then throw `NotSupportedException`.
- `Drop` — read the configuration first, remove the option, and name it in
  `PrintJobInfo.DroppedOptions`.

`Throw` and `Drop` need a known configuration to compare against. When the printer
reports an empty configuration, both act as `Send`, because there is nothing to
compare the option to.

**A printer from `PrinterFactory` owns nothing and needs no disposal.**
`PrinterFactory` keeps one internally managed `HttpClient` and shares it with every
`IppPrinter` it returns, so a printer obtained through `IPrinterFactory.Open` or
`OpenAsync` never owns an `HttpClient` of its own. Dispose `PrinterFactory` itself
instead, when you are done with it (the dependency injection registration does this
for you at shutdown).

**A directly constructed `IppPrinter` still owns its client.** `IPrinter` itself does
not extend `IDisposable`, so a variable typed as `IPrinter` gives no compile-time
reminder to dispose it. `new IppPrinter(endpoint)` builds and owns its own
`HttpClient`, unless you pass one in yourself. Check for `IDisposable` and dispose the
printer when it is present:

```csharp
if (printer is IDisposable disposable)
{
    disposable.Dispose();
}
```

## Transports

`IPrinterTransport` transmits payloads: `CanHandle(endpoint)` plus
`WriteAsync(endpoint, payload, cancellationToken)`.

- `TcpPrinterTransport` — sends to `NetworkPrinterEndpoint` printers over TCP.
  The connection timeout defaults to five seconds and is configurable through the
  constructor. Throws `NotSupportedException` for other endpoints.

```csharp
IPrinterTransport transport = new TcpPrinterTransport();
await transport.WriteAsync(new NetworkPrinterEndpoint("192.168.1.50"), payload, cancellationToken).ConfigureAwait(false);
```

## Printer Status over IPP

`IppPrinterStatusClient` reads identity and status from network printers via
IPP Get-Printer-Attributes. It tries IPPS (TLS) first and falls back to plain IPP
across `/ipp/print` and `/ipp/port1`. All operations are read-only.
The wire format is handled by `SharpIppNext`; see [Packages](packages.md#runtime-dependencies).

- Returns `IppPrinterDetails` (`PrinterInfo` + `PrinterStatus`): make and model,
  state, state reasons, and supply markers (ink/toner levels via `PrinterStatus.Markers`).
- `IppPrinterStatusClient` is `IDisposable`. The default constructor owns an
  `HttpClient`, and disposing the client disposes that `HttpClient`. A client built
  from a caller-supplied `HttpClient` does not dispose it.
- The default client accepts any server certificate, because network printers
  overwhelmingly use self-signed certificates. Supply your own `HttpClient`
  for custom validation.
- The IPP port (default 631) is independent of any raw print channel port.
- The optional `resourcePath` parameter is tried before the well-known paths. Pass the
  `rp` attribute of a DNS-SD TXT record to reach a printer that serves IPP elsewhere.
- Neither `IppPrinterStatusClient` nor `SnmpPrinterStatusClient` implements an
  interface. A test cannot mock either one directly; wrap the class you need behind
  your own seam if a test must replace it.

```csharp
IppPrinterStatusClient client = new();
IppPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken).ConfigureAwait(false);
```

## Job queues and progress

`IPrintJobQueue` inspects and manages the jobs of a printer: `GetJobsAsync`,
`GetJobAsync`, and `CancelJobAsync`. `CompositePrintJobQueue` is the general
implementation: it routes a `Spooler` identifier to the operating system spooler
and a `Network` identifier to IPP.

`IPrintJobMonitor.WatchJobAsync` watches one job and yields a reading each time its
state or progress changes, until the job reaches a terminal state or leaves the
queue. `PollingPrintJobMonitor` is the implementation: it reads the queue again and
again, so it works with every printer and needs no notification channel from the
printer.

```csharp
IPrintJobMonitor monitor = new PollingPrintJobMonitor(queue);
await foreach (var reading in monitor.WatchJobAsync(job.PrinterId, job.JobId, new PrintJobMonitorOptions(), cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

A caller can go from `MdnsPrinterDiscovery`, to `IPrinterFactory.Open`, to
`IPrinter.PrintAsync`, to `IPrintJobMonitor.WatchJobAsync`, using no other type.

## Discovery

- `IMdnsPrinterDiscovery.DiscoverPrintersAsync` — asks the local link for printers that
  advertise themselves. Prefer this: it needs no host list and opens no connection.
  `MdnsPrinterDiscovery` is the implementation. See [Discovery over mDNS](#discovery-over-mdns).
- `INetworkPrinterDiscovery.DiscoverNetworkPrintersAsync` — probes explicit hosts for
  an open raw print channel. Probing is opt-in: callers pass the hosts, port,
  per-host `ConnectTimeout`, and `MaxDegreeOfParallelism`. `TcpNetworkPrinterDiscovery`
  is the TCP-probe implementation. Use it for printers that do not advertise themselves.
- `IPrinterDiscovery.GetPrintersAsync` — enumerates printers installed in the operating
  system print spooler (Win32 print queues, CUPS destinations). `SpoolerPrinterDiscovery`
  is the implementation. See [Spooler](#spooler).

```csharp
INetworkPrinterDiscovery discovery = new TcpNetworkPrinterDiscovery();
NetworkPrinterDiscoveryOptions options = new()
{
    Hosts = ["192.168.1.50", "192.168.1.51"],
    ConnectTimeout = TimeSpan.FromSeconds(1),
};
IReadOnlyList<DiscoveredPrinter> printers =
    await discovery.DiscoverNetworkPrintersAsync(options, cancellationToken).ConfigureAwait(false);
```

## Discovery over mDNS

`MdnsPrinterDiscovery` sends one multicast DNS query to the local link and collects the
answers for `BrowseTimeout`, which defaults to two seconds. It replaces a subnet sweep:
no host list, no connection to any address.

```csharp
IMdnsPrinterDiscovery discovery = new MdnsPrinterDiscovery();
IReadOnlyList<DiscoveredPrinter> printers =
    await discovery.DiscoverPrintersAsync(new MdnsPrinterDiscoveryOptions(), cancellationToken).ConfigureAwait(false);
```

`MdnsPrinterDiscoveryOptions` controls the browse:

- `ServiceTypes` — defaults to `_pdl-datastream._tcp.local` (the raw port 9100 channel),
  `_ipp._tcp.local`, `_ipps._tcp.local`, and `_printer._tcp.local` (LPD).
- `BrowseTimeout` — how long to collect answers. Defaults to two seconds.
- `QueryRetries` — how many times to send the queries again during the browse, because a
  multicast datagram can be lost. Defaults to two.
- `NetworkInterfaceIndexes` — restricts the browse to named interfaces. Empty uses all.
- `IncludeIPv6` — adds the `ff02::fb` group to the IPv4 `224.0.0.251` group.

The TXT attributes of each answer fill `PrinterInfo`: `ty` becomes `Name`, `note` becomes
`Location`, and `pdl` becomes `DriverName`.

A printer advertises every protocol it supports under one service name, so the browse
reports it one time. When a printer offers more than one protocol, the endpoint is chosen
in this order, because only the raw channel can accept a `PrinterPayload` from
`TcpPrinterTransport`:

| Service type | Port | Why |
| --- | --- | --- |
| `_pdl-datastream._tcp` | 9100 | The raw channel. `TcpPrinterTransport` can print to it. |
| `_ipp._tcp`, `_ipps._tcp` | 631 | `IppPrinterStatusClient` can query it. |
| `_printer._tcp` | 515 | LPD needs a message format no transport here writes. |

The DNS wire format is handled by `Makaretu.Dns.New`; see
[Packages](packages.md#runtime-dependencies). The socket binds an ephemeral port, not port 5353. RFC 6762 §5.1 and §6.7 require a
responder to answer such a "one-shot" querier by unicast, so the browse never competes
with an operating system responder such as avahi or Bonjour. Note that the answer arrives
as unicast: a strict local firewall can discard it, and the browse then returns nothing
even though `avahi-browse` works.

## Spooler

`SpoolerPrinterDiscovery`, `SpoolerPrintJobQueue`, and `SpoolerPrinter` reach the
operating system print spooler through one of two drivers, chosen for you at
run time:

- `CupsSpoolerDriver` — used on Linux and macOS. CUPS runs its own IPP server on
  `localhost:631`, so this driver sends the same IPP requests `IppPrinter` and
  `IppPrintJobQueue` already use, aimed at the local daemon. It needs no native
  interop.
- `WindowsSpoolerDriver` — used on Windows, through `winspool.drv`. Windows has no
  local IPP server, so this driver is the one part of the library that calls
  native code.

The Windows driver honours only `PrintOptions.JobName` today. `Copies`, `Duplex`,
`ColorMode`, `Orientation`, `MediaSource`, `MediaSize`, and `ResolutionDpi` are not
mapped into a `DEVMODE`, so none of them changes what happens on Windows; a job
reaches the device unchanged. This matches `UnsupportedOptionBehavior.Send`: the
request goes out and the printer decides.

Native Windows calls cannot run in this repository's own test suite or CI, which
both run on Linux. [Windows Manual Tests](windows-manual-tests.md) lists what the
automated tests already prove and what a person must still check by hand on a real
Windows machine.

## Printer Status over SNMP

`SnmpPrinterStatusClient` reads the Printer MIB (RFC 3805) and the Host Resources MIB from
a host you already know. Many printers supply these and do not answer IPP, and SNMP also
reports the serial number and the page count, which IPP does not carry. All operations are
read-only.

```csharp
SnmpPrinterStatusClient client = new();
SnmpPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken).ConfigureAwait(false);
Console.WriteLine($"{details.Info.Name}: {details.SerialNumber}, {details.LifetimePageCount} pages");
```

- Returns `SnmpPrinterDetails` (`PrinterInfo` + `PrinterStatus`) plus `SerialNumber` and
  `LifetimePageCount`.
- `SnmpPrinterStatusOptions` sets `Community` (defaults to `public`), `RequestTimeout`
  (two seconds), and `Retries` (two, which gives three attempts, because UDP can lose a
  datagram). The client throws `InvalidOperationException` when no attempt is answered.
- `hrPrinterStatus` gives `PrinterStatus.State`. The bits of
  `hrPrinterDetectedErrorState` can raise it to `Error` or `Offline`, and every set bit is
  named in `PrinterStatus.Detail`. A bit that is only a warning, such as `lowToner`, does
  not change the state, because a printer low on toner still prints.
- Supply levels come from `prtMarkerSuppliesTable`. The Printer MIB uses `-1`, `-2` and
  `-3` to report a level that is not a quantity, and `PrinterMarker.LevelPercent` is then
  `null`.
- **This client does not search a network.** Find printers with `IMdnsPrinterDiscovery` or
  `INetworkPrinterDiscovery` first.
- Only SNMP version 2c is supported. SNMPv3, which adds authentication and privacy, is not.
- The messages are encoded by `Lextm.SharpSnmpLib`, but the datagrams travel over our own
  UDP channel, which keeps the timeout and the retry behaviour under our control. That
  library is taken at a prerelease version on purpose; see
  [Packages](packages.md#approved-exception-a-prerelease-snmp-dependency).

The SNMP port (default 161) is independent of the raw print channel and of the IPP port.

## Dependency Injection

`AdaptArch.Devices.DependencyInjection` provides `AddDevices()` and `AddPrinters()`,
registering `TcpPrinterTransport`, `TcpNetworkPrinterDiscovery`, `MdnsPrinterDiscovery`,
`IppPrinterStatusClient`, `SnmpPrinterStatusClient`, `PrinterFactory`,
`SpoolerPrinterDiscovery`, `CompositePrintJobQueue`, and `PollingPrintJobMonitor` as
singletons. All of these services hold no per-call or per-caller state, so one shared
instance is safe for the life of the application:

```csharp
services.AddPrinters();
```

## Not Yet Implemented

- USB transport (endpoint model exists; transmission needs platform drivers).
- SNMP version 3, which adds authentication and privacy.
- IPP notifications (`Create-Printer-Subscriptions`, `Get-Notifications`).
- An LPD transport.
