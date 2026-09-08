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

## Payloads

`PrinterPayload` carries `ReadOnlyMemory<byte> Data` plus a `ContentType` string so
transports and spoolers can route it correctly.

- `PrinterContentTypes` — constants for `Zpl`, `Epl`, `Cpcl`, `EscPos`, `Text`, `OctetStream`.
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
- `PrinterConfiguration` — supported DPIs, duplex/color support, media sizes.

## Transports

`IPrinterTransport` transmits payloads: `CanHandle(endpoint)` plus
`WriteAsync(endpoint, payload, cancellationToken)`.

- `TcpPrinterTransport` — sends to `NetworkPrinterEndpoint` printers over TCP
  with a configurable connection timeout. Throws `NotSupportedException` for other endpoints.

```csharp
IPrinterTransport transport = new TcpPrinterTransport();
await transport.WriteAsync(new NetworkPrinterEndpoint("192.168.1.50"), payload, cancellationToken).ConfigureAwait(false);
```

## Discovery and Job Queues

- `IPrinterDiscovery.GetPrintersAsync` — enumerates OS spooler printers (Win32/CUPS).
  No implementation ships yet; the interface is the seam for platform drivers.
- `INetworkPrinterDiscovery.DiscoverNetworkPrintersAsync` — probes explicit hosts for
  an open raw print channel. Probing is opt-in: callers pass the hosts, port,
  per-host `ConnectTimeout`, and `MaxDegreeOfParallelism`. `TcpNetworkPrinterDiscovery`
  is the TCP-probe implementation.
- `IPrintJobQueue` — `GetJobsAsync`, `GetJobAsync`, `CancelJobAsync` per printer.

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

## Dependency Injection

`AdaptArch.Devices.DependencyInjection` provides `AddDevices()` and `AddPrinters()`,
registering `TcpPrinterTransport` and `TcpNetworkPrinterDiscovery` as singletons:

```csharp
services.AddPrinters();
```

## Not Yet Implemented

- OS spooler enumeration (`IPrinterDiscovery`) and job queue (`IPrintJobQueue`) implementations.
- USB transport (endpoint model exists; transmission needs platform drivers).
- mDNS/SNMP network discovery beyond TCP probing.
