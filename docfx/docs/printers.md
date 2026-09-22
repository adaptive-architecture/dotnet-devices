# Printers

The `AdaptArch.Devices` package holds cross-platform printer abstractions
(`AdaptArch.Devices.Printing` namespace). They separate *where* a printer is (endpoints)
from *how* the bytes get there (transports), and model print data as raw bytes with a
content type.

## Identifiers

`PrinterId` is a URI, `{scheme}://{authority}`. The scheme names the transport channel, so
nothing has to guess a channel from a port number.

| Scheme | Channel | Default port |
| :--- | :--- | :--- |
| `raw` | The raw TCP channel. Sends the payload unchanged, gives back no job identifier. | 9100 |
| `ipp` | The Internet Printing Protocol. | 631 |
| `ipps` | IPP over TLS. Advertised separately by DNS-SD, so a caller can demand it. | 631 |
| `spooler` | A print queue of the operating system. | none |
| `cups` | A print queue of a CUPS server, reached over the network. | 631 |

The authority is the identity the device reported about itself when there is one, and
otherwise the address that opens the channel:

```text
raw://192.168.1.5                                the raw channel, port 9100 implied
raw://192.168.1.5:9101                           a port that is not the default
ipp://192.168.1.5                                the IPP channel, port 631 implied
ipps://[2001:db8::5]:8631                        an IPv6 literal is bracketed
ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242       the identity form
spooler://EPSON_L6270                            a print queue
spooler://%5C%5Cserver%5Cqueue                   a Windows connection name, escaped
cups://printsrv/EPSON_L6270                      a queue of a CUPS server
cups://printsrv:8631/Front%20Desk                a port that is not the default, and an escaped name
```

**An address form can be opened with no discovery**, because the scheme gives the endpoint
type and the default port. An **identity form** names no address and has to be resolved
through `IPrinterManager`, which knows where the device was found. `IsDeviceIdentity` says
which of the two you hold.

**`DeviceKey` is the authority with any `:port` stripped**, so `raw://192.168.1.5` and
`ipp://192.168.1.5` are one device. A `cups` key keeps the server *and* the queue, because
one server holds many queues and its host alone would fuse a whole fleet into one device.

Only a UUID is ever written as an identity authority: a serial number reads exactly like a
host name or a queue name, so putting one there would make the text ambiguous. A serial
number still groups the channels of a device, through `PrinterDeviceKey`.

`Parse`, `TryParse` and `ToString` round-trip. `TryGetHost`, `TryGetQueueName`,
`TryGetDeviceIdentity` and `TryCreateEndpoint` read the parts — and a call site that wants a
host gets `false` for an identity form, which is exactly the case that needs a discovery.

## Endpoints

`PrinterEndpoint` describes reachability as pure data, and it carries its scheme rather than
letting the port imply it:

- `NetworkPrinterEndpoint { Host, Scheme, Port }` — built with `NetworkPrinterEndpoint.Raw`,
  `.Ipp` or `.Ipps`. There is no port-only constructor: reading the channel back out of the
  port number is how a raw channel and an IPP channel came to be confused with each other.
- `SpoolerPrinterEndpoint { Name }` — a print queue of the operating system.
- `CupsPrinterEndpoint { Host, Name, Port }` — one queue of a CUPS server. It carries a host
  and is still not a `NetworkPrinterEndpoint`: that one addresses a device, and this one
  addresses one queue of a server that may hold hundreds.

There is no USB endpoint and no `usb` scheme; see [Roadmap](roadmap.md#usb-printers).

## Channels and devices

- `DiscoveredPrinter` is one **channel**, with the capabilities (`null` when they were not
  read), the print options that channel applies, the devices a source vouched it belongs to,
  and `HasJobQueue` / `GivesPassthrough`.
- `PrinterDevice` is one **physical printer**, with `Channels`, `ChannelsByTransport` and
  `Accepts`, which says whether one of its channels reads a content type.
  `IPrinterManager.DiscoverAsync` returns these.
- `PrinterDeviceDetails` merges what every channel reported, plus which discoveries found the
  device and which read-only protocols answered.

```csharp
foreach (var device in devices)
{
    Console.WriteLine($"{device.Details.Name} — {device.Key}");
    foreach ((var transport, var channels) in device.ChannelsByTransport)
    {
        foreach (var channel in channels)
        {
            Console.WriteLine($"  {transport}: {channel.Id} applies {channel.SupportedOptions}");
        }
    }
}
```

## Payloads

`PrinterPayload` carries bytes plus a `ContentType`, so transports and spoolers can route it
correctly. `PrinterContentTypes` holds the constants (`Zpl`, `Epl`, `Cpcl`, `EscPos`, `Text`,
`Png`, `Jpeg`, `Pdf`, `OctetStream`).

```csharp
PrinterPayload payload = PrinterPayload.FromString(
    "^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
```

## Printing

`IPrinter` is the main seam: `PrintAsync`, `GetStatusAsync` and `GetConfigurationAsync`. Mock
it in a unit test instead of touching hardware. `PrintOptions` carries the optional per-job
settings, and an unset property falls back to the printer default, which keeps a job portable
across spoolers with different capabilities.

Three rules are not obvious from the member names:

- **An empty configuration means "not known", not "nothing supported".** `SupportsDuplex`,
  `SupportsColor` and `SupportsPageRanges` are `bool?`: `null` means the printer did not
  report the capability, and `false` means it denied it. A `RawPrinter` always reports an
  empty configuration, because the raw channel gives no way to ask.
- **Only SNMP fills `SerialNumber` and `LifetimePageCount`.** A printer with no status channel
  reports the state `Unknown`.
- **`Media` and `MediaSources` carry the Windows number beside each name**, because a device
  mode names a size with a `DMPAPER_*` number and a tray with a `DMBIN_*` number. Every other
  channel reports a `null` number.

### Sending a job

`IPrinterFactory` turns a channel, or a bare `PrinterId`, into a ready `IPrinter` for the
transport the scheme names. Nothing is probed: the scheme states the channel.

```csharp
IPrinterFactory factory = new PrinterFactory();
IPrinter printer = await factory.OpenAsync(PrinterId.ForIpp("192.168.1.50"), cancellationToken)
    .ConfigureAwait(false);
PrintJobInfo job = await printer.PrintAsync(payload, options: null, cancellationToken)
    .ConfigureAwait(false);
```

`OpenAsync` throws `NotSupportedException` for an identity form, because such an identifier
names no endpoint. Resolve it through `IPrinterManager` first.

Most applications should not open a channel by hand at all. [The printer
manager](printer-manager.md) picks the channel each call needs, which is usually the right
one and is never the wrong one for the payload.

### An option the printer does not support

`PrintOptions.OnUnsupported` says what to do with one:

- `Send` (the default) — send the option and let the printer decide. This costs no extra
  request.
- `Throw` — read the configuration first, then throw `NotSupportedException`.
- `Drop` — read the configuration first, remove the option, and name it in
  `PrintJobInfo.DroppedOptions`.

`Throw` and `Drop` need a known configuration to compare against, so both act as `Send` when
the printer reports an empty one. An explicit `false` is a capability statement, and both
judge against it.

### Disposal

**A printer from `PrinterFactory` owns nothing and needs no disposal.** The factory keeps one
`HttpClient` and one of each status client and shares them with every printer it returns.
Dispose the factory instead; the dependency injection registration does this at shutdown, and
afterwards both `Open` methods throw `ObjectDisposedException`.

**A directly constructed `IppPrinter` still owns its client.** `IPrinter` does not extend
`IDisposable`, so nothing reminds you. Check for `IDisposable` and dispose it when present.

The model types are read-only after construction, and the manager hands out its cached
instances, so a caller cannot change what other callers see.

## Transports

`IPrinterTransport` transmits payloads. `TcpPrinterTransport` sends to a
`NetworkPrinterEndpoint` over TCP and throws `NotSupportedException` for other endpoints. Its
timeout defaults to five seconds and applies to the connection, and then again to the write,
so a printer that accepts the connection but stops reading fails with `TimeoutException`
instead of blocking the caller. After the write the transport closes its side, which tells the
printer that the job is complete.

## Related pages

- [Document formats](document-formats.md) — what is sent for each content type.
- [Page placement](page-placement.md) — where a rendered page lands on the media.
- [Discovery](discovery.md) — finding printers without a host list.
- [Printer manager](printer-manager.md) — one entry point that runs every source and picks a
  channel.
- [Spooler and CUPS](spooler-and-cups.md) — the operating system queue, and a CUPS server over
  the network.
- [Status and monitoring](status-and-monitoring.md) — reading state, and watching a job.
