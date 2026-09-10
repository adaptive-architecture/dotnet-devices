# Printers

The `AdaptArch.Devices` package holds cross-platform printer abstractions
(`AdaptArch.Devices.Printing` namespace). They separate *where* a printer is (endpoints)
from *how* the bytes get there (transports).

## Concepts

- **Identifier** — `PrinterId` is a URI, `{scheme}://{authority}`. The scheme names the
  transport channel (`raw`, `ipp`, `ipps` or `spooler`), and the authority is the identity
  the device reported or, failing that, the address that opens the channel:
  `raw://192.168.1.5`, `spooler://EPSON_L6270`,
  `ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242`.
- **Devices and channels** — a printer is usually reachable more than one way, and each way
  is a `DiscoveredPrinter` channel. `PrinterDevice` is the physical printer, with every
  channel grouped by transport.
- **Payloads** — `PrinterPayload` carries bytes plus a content type. `PrinterContentTypes`
  holds the constants for ZPL, EPL, CPCL, ESC/POS, plain text, PNG and PDF.
- **Printing** — `IPrinter` gives `PrintAsync`, `GetStatusAsync` and
  `GetConfigurationAsync`. `PrintOptions` carries the per-job settings, and an unset
  property falls back to the printer default.
- **Discovery** — an mDNS browse that needs no host list, a TCP probe of explicit hosts,
  and the printers installed in the operating system spooler.
- **Status** — `IppPrinterStatusClient` reads state and supply levels over IPP.
  `SnmpPrinterStatusClient` reads the Printer MIB over SNMP version 2c, which reaches
  printers that do not answer IPP and adds the serial number and the page count. Both are
  read-only.
- **The printer manager** — `IPrinterManager` is the layer most callers want. It runs every
  discovery source, reports one `PrinterDevice` for each physical printer, and picks the
  channel each call needs.
- **USB printers** — print to one through its operating system queue, with a `spooler://`
  identifier. There is no `usb` scheme: each operating system claims the device with its
  own driver, and direct access would break the print path the machine already uses.

## Example

```csharp
// Find every reachable printer, with the channels that reach each one.
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();
IReadOnlyList<PrinterDevice> devices = await manager.DiscoverAsync(null, cancellationToken);

foreach (var device in devices)
{
    Console.WriteLine($"{device.Details.Name} — {device.Key}");
    foreach ((var transport, var channels) in device.ChannelsByTransport)
    {
        Console.WriteLine($"  {transport}: {String.Join(", ", channels.Select(c => c.Id))}");
    }
}

// A printer command language needs a channel that sends the bytes unchanged. The
// manager picks one from anywhere on the device, and refuses rather than guessing.
PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
await manager.PrintAsync(devices[0].Id, payload, new PrintOptions { RequirePassthrough = true }, cancellationToken);
```

Register the services with `services.AddPrinters()` from the
`AdaptArch.Devices.DependencyInjection` package.

The API reference holds every member. The rules behind these choices — the document format
sent for each peer, how channels are grouped into devices, and the transport policy — are
in [the repository documentation](https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/printers.md).
