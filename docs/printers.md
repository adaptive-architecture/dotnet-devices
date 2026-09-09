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
- `PrinterStatus` also carries `SerialNumber` and `LifetimePageCount`. A value is
  `null` when the printer did not report it. Only SNMP fills these two fields today.
  IPP and the operating system spooler do not report them yet.
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
Console.WriteLine($"{details.Info.Name}: {details.Status.SerialNumber}, {details.Status.LifetimePageCount} pages");
```

- Returns `SnmpPrinterDetails` (`PrinterInfo` + `PrinterStatus`). The serial number and
  the lifetime page count are on `PrinterStatus.SerialNumber` and
  `PrinterStatus.LifetimePageCount`.
- `SnmpPrinterStatusOptions` sets `Community` (defaults to `public`), `RequestTimeout`
  (two seconds), and `Retries` (two, which gives three attempts, because UDP can lose a
  datagram). The client throws `InvalidOperationException` when no attempt is answered.
- `hrPrinterStatus` gives `PrinterStatus.State`. The bits of
  `hrPrinterDetectedErrorState` can raise it to `Error` or `Offline`, and every set bit is
  named in `PrinterStatus.Detail`. A bit that is only a warning, such as `lowToner`, does
  not change the state, because a printer low on toner still prints.
- Supply levels come from `prtMarkerSuppliesTable`. `PrinterMarker.LevelRaw` and
  `PrinterMarker.MaxCapacity` hold the raw reported numbers. `PrinterMarker.LevelPercent`
  holds the computed percent. The Printer MIB uses a negative level for a value that is
  not a quantity. `-1` means "other". `-2` means "unknown amount remains". `-3` means
  "some amount remains". For a negative level, `LevelPercent` is `null`. `LevelRaw` still
  holds the reported negative number.
- **This client does not search a network.** Find printers with `IMdnsPrinterDiscovery` or
  `INetworkPrinterDiscovery` first.
- Only SNMP version 2c is supported. SNMPv3, which adds authentication and privacy, is not.
- The messages are encoded by `Lextm.SharpSnmpLib`, but the datagrams travel over our own
  UDP channel, which keeps the timeout and the retry behaviour under our control. That
  library is taken at a prerelease version on purpose; see
  [Packages](packages.md#approved-exception-a-prerelease-snmp-dependency).

The SNMP port (default 161) is independent of the raw print channel and of the IPP port.

## Printer manager

`IPrinterManager` gives one entry point for printing. `DiscoverAsync` finds printers.
`PrintAsync` prints to one printer by its identifier.

`DiscoverAsync` runs three discovery sources: the mDNS browse, the spooler enumeration,
and the TCP probe. Each source has its own switch on `PrinterManagerOptions`.
`IncludeMdns` turns the mDNS browse on or off. It defaults to `true`. `IncludeSpooler`
turns the spooler enumeration on or off. It defaults to `true`. **The TCP probe never
runs on its own.** It needs a host list, so it runs only when `Probe` carries one.
To change how long the browse waits for answers, set `Mdns.BrowseTimeout`. Do not
look for a timeout on `PrinterManagerOptions` itself; the timeout lives on `Mdns`.

**All three sources can run, or none of them.** When every source is off,
`DiscoverAsync` returns an empty list. It does not throw. No source ran, so no
source failed.

A source that fails does not fail the call. The manager reports what the other sources
found.

Every result carries a `DiscoverySource`. Two entries can describe one physical device:
one from the browse, and one from the spooler. The manager keeps these two entries
separate on purpose. For a printer language such as ZPL, the raw channel sends the
bytes unchanged. The spooler queue may not send the bytes unchanged.

**The mDNS browse and the network probe do not always keep separate identifiers.**
`MdnsRecordAssembler` builds `PrinterId.FromNetwork(host)`. `TcpNetworkPrinterDiscovery`
builds `new PrinterId(PrinterIdKind.Network, host)`. For the same host, these two
identifiers are equal. When a host is in `Probe.Hosts` and the same host also answers
the mDNS browse, the two entries share one identifier. The cache then keeps only the
last entry written, which is the probe's entry.

`PrintAsync` keeps what discovery found. On an unknown identifier, it runs one fresh
discovery. It throws only when the identifier is still unknown after that.

**A stale entry does not repair itself.** When a printer keeps its identifier but
changes address, the print fails with the transport error. The manager does not
re-discover after a failed print. Most print failures are not addressing problems.
Examples are no paper, no permission, and a rejected option. Call `DiscoverAsync` to
refresh.

**`DiscoverAsync` does not remove cache entries.** It adds new entries and updates
existing entries. A printer removed from the network stays in the cache. Every print
to that printer then keeps failing.

**`PrintOptions.RequirePassthrough` refuses rather than guesses.** Set it for ZPL, EPL,
CPCL and ESC/POS. A raw TCP channel and the Windows spooler send the bytes unchanged.
IPP, IPPS and CUPS can filter or rasterise the bytes, so they cannot give that promise.
When the printer has no channel that sends the bytes unchanged, the manager throws
`NotSupportedException`, instead of sending the bytes somewhere that would change them.

```csharp
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();
var printers = await manager.DiscoverAsync(null, cancellationToken).ConfigureAwait(false);
var label = printers.First(p => p.Info.Name.Contains("Zebra", StringComparison.Ordinal));

PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
await manager.PrintAsync(
    label.Id,
    payload,
    new PrintOptions { RequirePassthrough = true },
    cancellationToken).ConfigureAwait(false);
```

**`GetStatusAsync` reads the current status of one printer.** It resolves the
identifier the same way `PrintAsync` does. It uses the cache first. On an unknown
identifier, it runs one fresh discovery. It throws `InvalidOperationException` when
the identifier is still unknown after that.

`GetStatusAsync` opens the printer through the same factory `PrintAsync` uses. It
reads the status, then closes the printer. It throws `NotSupportedException` when
the factory does not support the endpoint.

```csharp
var status = await manager.GetStatusAsync(label.Id, cancellationToken).ConfigureAwait(false);
Console.WriteLine($"{status.State}: {status.Detail}");
```

**`WatchJobAsync` finds the printer, then checks it for a job queue.** It resolves
the identifier the same way `PrintAsync` does. It reads the resolved endpoint before
it watches anything.

- A network endpoint on the IPP port (631) has a job queue. A spooler endpoint has a
  job queue on every platform. `WatchJobAsync` watches both.
- A network endpoint on any other port has no job queue. The raw port 9100 is an
  example. Printers for ZPL and EPL labels often use this port. `WatchJobAsync`
  throws `NotSupportedException` for this endpoint. The message names the printer.

This refusal is correct. It is not a limitation. A raw channel gives back no real
job identifier. It gives back a generated one instead. The raw port has no queue to
read a status from. Watching such a job through IPP would ask the wrong protocol,
on a port that may not be open, about a job it never saw. A past defect showed the
cost of this: the wrong queue gave back no answer for the job. The manager read
that empty answer as "the job is done". The manager told the caller the label was
done before the printer did any work.

```csharp
await foreach (var reading in manager.WatchJobAsync(
    label.Id, job.JobId, new PrintJobMonitorOptions(), cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

## Dependency Injection

`AdaptArch.Devices.DependencyInjection` provides `AddDevices()` and `AddPrinters()`.
`AddPrinters()` registers these types as singletons: `TcpPrinterTransport`,
`TcpNetworkPrinterDiscovery`, `MdnsPrinterDiscovery`, `IppPrinterStatusClient`,
`SnmpPrinterStatusClient`, `PrinterFactory`, `SpoolerPrinterDiscovery`,
`SpoolerPrintJobQueue`, `CompositePrintJobQueue`, and `PollingPrintJobMonitor`. Most of
these types hold no state between calls. Two types hold state, and the state is safe
to share. `CompositePrintJobQueue` keeps one queue per network host, in a thread-safe
dictionary. `SnmpPrinterStatusClient` keeps a request counter, and it updates the
counter with an atomic operation. One shared instance of each type is safe for the
life of the application.

`AddPrinters()` also registers `PrinterManager` as a singleton, for a different reason.
`PrinterManager` keeps a shared discovery cache and a semaphore on purpose. This shared
state is why `PrinterManager` must stay a singleton too:

```csharp
services.AddPrinters();
```

## Sample

`samples/Devices.Samples` has three scenarios. Each one shows a different layer.

- `print-manager` — uses `IPrinterManager` for discovery, status, sending, watching,
  and ZPL. This is the layer most callers want. `discover` also prints the status of
  each printer, read through `GetStatusAsync`.
- `manual-management` — uses `IMdnsPrinterDiscovery`, `INetworkPrinterDiscovery`, and
  `IPrinterTransport` directly. This shows the seams the manager sits on.
- `win-printer-test` — reads a Windows print queue through `SpoolerPrinter` and
  `SpoolerPrintJobQueue`, and prints the evidence for
  [Windows manual tests](windows-manual-tests.md). It needs Windows.

`print-manager send`, `print-manager watch`, `print-manager zpl`, and
`manual-management status` take a printer identifier, not a bare host. A printer
identifier has the form `Kind:Value`, for example `Network:192.168.0.152` or
`Spooler:EPSON_L6270_Series`. `discover` prints the exact identifier to use for
each printer it finds. The sample reads a value with no `Kind:` prefix as a network
printer, so a plain IP address or host name still works.

`manual-management send` writes raw bytes over a network channel, so it cannot
reach a spooler queue. Given a spooler identifier, it prints a message and sends
nothing. Use `print-manager send` for a spooler queue.

Run `dotnet run --` with no arguments for the full command list.

## Not Yet Implemented

- USB transport (endpoint model exists; transmission needs platform drivers).
- SNMP version 3, which adds authentication and privacy.
- IPP notifications (`Create-Printer-Subscriptions`, `Get-Notifications`).
- An LPD transport.
