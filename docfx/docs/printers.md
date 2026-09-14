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
// content type says so, and the manager picks such a channel from anywhere on the
// device. There is nothing to switch on.
PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
await manager.PrintAsync(devices[0].Id, payload, null, cancellationToken);
```

Register the services with `services.AddPrinters()` from the
`AdaptArch.Devices.DependencyInjection` package.

## Choosing the sources and the channel

`PrinterManagerOptions` controls which discovery sources run. Give it to each
`DiscoverAsync` call, because the manager does not keep it.

| Source | Property | Default |
| --- | --- | --- |
| Operating system spooler | `IncludeSpooler` | On |
| mDNS browse | `IncludeMdns`, and `Mdns.ServiceTypes` for each service type | On |
| TCP probe | `Probe`, which lists the hosts to open a connection to | Off |

Two more properties ask each channel for more data. Each one costs one request per channel.

| Property | What it adds |
| --- | --- |
| `ReadIdentity` | The UUID, the serial number and the device URI. This data groups a queue and the network channels into one device. |
| `ReadCapabilities` | The document formats, the media, the resolutions and the duplex support of each channel. |

`Transports` is different from the properties above: it does not change what discovery
finds, but which channels the manager may open. Give it to the constructor, or to
`AddPrinters`, because a print carries no options of its own.

```csharp
services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);
```

Membership is permission. Position is preference, but only between the channels that suit
a call equally: the payload decides first, or an order that put IPP before the raw channel
would send each label to a channel that converts it.

### Use the printers in the spooler only

Switch the other two sources off. The cache then holds spooler channels only, so each call
uses the spooler.

```csharp
PrinterManagerOptions spoolerOnly = new() { IncludeMdns = false };
IReadOnlyList<PrinterDevice> devices = await manager.DiscoverAsync(spoolerOnly, cancellationToken);
```

### Get all the data, but print through the spooler

A queue reports little about the hardware. To get more, let each source run, and set both
`Read*` properties. `ReadIdentity` groups the queue with the network channels of the same
printer, and `PrinterDevice.Details` then holds what all of them reported.

```csharp
PrinterManagerOptions rich = new()
{
    Probe = new() { Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts() },
    ReadIdentity = true,
    ReadCapabilities = true,
};

IReadOnlyList<PrinterDevice> devices = await manager.DiscoverAsync(rich, cancellationToken);
```

Then let the manager open the spooler only:

```csharp
services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);
```

Each call then uses the queue, for each format and on each operating system. Discovery is
not affected, so `PrinterDevice.Channels` still lists the raw and the IPP channels, and
`PrinterDevice.Details` still holds what they reported.

```csharp
await manager.PrintAsync(device.Id, payload, null, cancellationToken);
```

A device that no allowed transport reaches causes a `NotSupportedException`. The message
gives the transports the manager may open.

### Select a channel for one call

To keep the default policy and select a channel for one call, give the identifier of that
channel instead of `device.Id`:

```csharp
var queue = device.ChannelsByTransport[PrinterScheme.Spooler][0];
await manager.PrintAsync(queue.Id, payload, null, cancellationToken);
```

**The content type decides before the identifier does.** On Windows this prints through the
spooler for each format, because the spooler sends the bytes unchanged. On Linux and macOS
a CUPS queue does not send the bytes unchanged, so a printer language (ZPL, EPL, CPCL or
ESC/POS) goes to the raw channel instead. Set `Transports` when you must have the queue.

To select a channel and obey nothing else, open it with `IPrinterFactory`. This is the same
abstraction, but the caller selects the channel instead of the manager:

```csharp
var printer = factory.Open(queue);
try
{
    PrintJobInfo job = await printer.PrintAsync(payload, options, cancellationToken);
    await foreach (var reading in monitor.WatchJobAsync(queue.Id, job.JobId, new(), cancellationToken))
    {
        Console.WriteLine(reading.State);
    }
}
finally
{
    (printer as IDisposable)?.Dispose();
}
```

`AddPrinters()` registers `IPrinterFactory` and `IPrintJobMonitor`. Inject them together
with `IPrinterManager`.

The API reference holds every member. The rules behind these choices — the document format
sent for each peer, how channels are grouped into devices, and the transport policy — are
in [the repository documentation](https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/printers.md).
