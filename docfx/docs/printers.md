# Printers

The `AdaptArch.Devices` package provides cross-platform printer abstractions
(`AdaptArch.Devices.Printing` namespace) that separate *where* a printer is
(endpoints) from *how* bytes get there (transports).

## Concepts

- **Identity and endpoints** — `PrinterId` pairs a `PrinterIdKind` (`Spooler`, `Network`, `Usb`)
  with a value. `PrinterEndpoint` describes reachability as pure data:
  `NetworkPrinterEndpoint` (TCP host/port, default 9100), `UsbPrinterEndpoint`
  (USB descriptors), or `SpoolerPrinterEndpoint` (OS queue name).
- **Payloads** — `PrinterPayload` carries raw bytes plus a content type.
  `PrinterContentTypes` defines constants for ZPL, EPL, CPCL, ESC/POS, plain text,
  and opaque octet streams. Build payloads with `FromString` (text-based languages)
  or `FromBytes` (pre-encoded streams).
- **Printing** — `IPrinter` exposes `PrintAsync`, `GetStatusAsync`, and
  `GetConfigurationAsync`. `PrintOptions` carries optional per-job settings
  (copies, duplex, color, orientation, media, resolution, job name); unset
  properties fall back to printer defaults.
- **Transports** — `IPrinterTransport` transmits payloads. `TcpPrinterTransport`
  sends to network endpoints over TCP with a configurable connection timeout.
- **Discovery and queues** — `IMdnsPrinterDiscovery` asks the local link for printers that
  advertise themselves (`MdnsPrinterDiscovery`), which needs no host list;
  `INetworkPrinterDiscovery` probes explicit hosts for an open raw print channel
  (`TcpNetworkPrinterDiscovery`); `IPrinterDiscovery` enumerates OS spooler printers;
  `IPrintJobQueue` inspects and cancels jobs.
- **Status** — `IppPrinterStatusClient` reads state and supply levels over IPP.
  `SnmpPrinterStatusClient` reads the Printer MIB over SNMP version 2c, which reaches
  printers that do not answer IPP and adds the serial number and the page count.
  Both are read-only.

## Example

```csharp
// Find the printers that advertise themselves on the local link.
IMdnsPrinterDiscovery discovery = new MdnsPrinterDiscovery();
IReadOnlyList<DiscoveredPrinter> printers =
    await discovery.DiscoverPrintersAsync(new MdnsPrinterDiscoveryOptions(), cancellationToken);

PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

IPrinterTransport transport = new TcpPrinterTransport();
await transport.WriteAsync(new NetworkPrinterEndpoint("192.168.1.50"), payload, cancellationToken);
```

## Dependency Injection

The `AdaptArch.Devices.DependencyInjection` package registers printer services
(`services.AddPrinters()`), keeping the core package free of runtime dependencies.
