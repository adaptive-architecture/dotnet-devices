# Printers

Cross-platform printer abstractions in `AdaptArch.Devices` (`AdaptArch.Devices.Printing`
namespace). The design separates *where* a printer is (endpoints) from *how* bytes get
there (transports), and models print data as raw bytes with a content type.

This page holds the rules and the reasons. For the members of each type, read the
generated API reference. Dependency injection lives in the separate
`AdaptArch.Devices.DependencyInjection` package.

## Identity and endpoints

A printer is normally reachable more than one way. Each way is a **channel**, and the
device is what the channels have in common.

### The identifier

`PrinterId` is a URI, `{scheme}://{authority}`. The scheme names the transport channel,
so nothing has to guess a channel from a port number.

| Scheme | Channel | Default port |
| --- | --- | --- |
| `raw` | The raw TCP channel. Sends the payload unchanged, gives back no job identifier. | 9100 |
| `ipp` | The Internet Printing Protocol. | 631 |
| `ipps` | IPP over TLS. Advertised separately by DNS-SD, so a caller can demand it. | 631 |
| `spooler` | A print queue of the operating system. | none |

The authority is the identity the device reported about itself when there is one, and
otherwise the address that opens the channel:

```text
raw://192.168.1.5                              the raw channel, port 9100 implied
raw://192.168.1.5:9101                         a port that is not the default
ipp://192.168.1.5                              the IPP channel, port 631 implied
ipps://[2001:db8::5]:8631                      an IPv6 literal is bracketed
ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242     the identity form
spooler://EPSON_L6270                          a print queue
spooler://%5C%5Cserver%5Cqueue                 a Windows connection name, escaped
```

- **An address form can be opened with no discovery.** The scheme gives the endpoint type
  and the default port. An **identity form** names no address and is resolved through
  `IPrinterManager`, which knows where the device was found. `IsDeviceIdentity` says which
  of the two it is, and takes no part in equality.
- **`DeviceKey` is the authority with any `:port` stripped.** The port belongs to the
  channel, not to the device, which is why `raw://192.168.1.5` and `ipp://192.168.1.5`
  are one device.
- **Only a UUID is written as an identity authority.** A serial number reads exactly like
  a host name or a queue name, so putting one in the authority would make the text
  ambiguous. A serial number still groups the channels of a device, through
  `PrinterDeviceKey`.
- `Parse`, `TryParse` and `ToString` round-trip. A port equal to the default of the scheme
  is dropped, a queue name is percent-escaped, and a `urn:uuid:` prefix is not accepted:
  it is an input to `PrinterId.ForDeviceUuid`, never a canonical identifier.
- `TryGetHost`, `TryGetQueueName`, `TryGetDeviceIdentity` and `TryCreateEndpoint` read the
  parts. A call site that wants a host gets `false` for an identity form, which is exactly
  the case that needs a discovery.

### Endpoints

`PrinterEndpoint` describes reachability as pure data, and it carries its scheme rather
than letting the port imply it:

- `NetworkPrinterEndpoint { Host, Scheme, Port }` — built with `NetworkPrinterEndpoint.Raw`,
  `.Ipp` or `.Ipps`. There is no port-only constructor: reading the channel back out of the
  port number is how a raw channel and an IPP channel came to be confused with each other.
- `SpoolerPrinterEndpoint { Name }` — a print queue of the operating system.

There is no USB endpoint and no `usb` scheme; see [USB printers](#usb-printers).

### Channels and devices

- `DiscoveredPrinter` is one **channel**, with the capabilities (`null` when not read), the
  print options the channel applies, the devices a source vouched it belongs to, and
  `HasJobQueue` / `GivesPassthrough`.
- `PrinterDevice` is one **physical printer**, with `Channels`, `ChannelsByTransport` and
  `Accepts`, which says whether one of its channels reads a content type.
  `IPrinterManager.DiscoverAsync` returns these.
- `PrinterDeviceDetails` merges what every channel reported, plus which discoveries found
  the device and which read-only protocols answered.

## Payloads

`PrinterPayload` carries bytes plus a `ContentType`, so transports and spoolers can route
it correctly. `PrinterContentTypes` holds the constants (`Zpl`, `Epl`, `Cpcl`, `EscPos`,
`Text`, `Png`, `Jpeg`, `Pdf`, `OctetStream`).

```csharp
PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
```

## Printing

`IPrinter` is the main seam: `PrintAsync`, `GetStatusAsync` and `GetConfigurationAsync`.
Mock it in unit tests instead of touching hardware. `PrintOptions` carries the optional
per-job settings, and an unset property falls back to the printer default, which keeps a
job portable across spoolers with different capabilities.

Three rules are not obvious from the member names:

- **An empty configuration means "not known", not "nothing supported".** `SupportsDuplex`,
  `SupportsColor` and `SupportsPageRanges` are `bool?`, so `null` means the printer did not
  report the capability and `false` means it denied it. A `RawPrinter` always reports an
  empty configuration, because the raw channel gives no way to ask.
- **Only SNMP fills `SerialNumber` and `LifetimePageCount`.** A printer without a status
  channel reports the state `Unknown`.
- **`Media` and `MediaSources` carry the Windows number beside each name**, because a device
  mode names a size with a `DMPAPER_*` number and a tray with a `DMBIN_*` number. Every
  other channel reports a `null` number.

### Sending a job

`IPrinterFactory` turns a channel, or a bare `PrinterId`, into a ready `IPrinter` for the
transport the scheme names. Nothing is probed: the scheme states the channel.

```csharp
IPrinterFactory factory = new PrinterFactory();
IPrinter printer = await factory.OpenAsync(PrinterId.ForIpp("192.168.1.50"), cancellationToken).ConfigureAwait(false);
PrintJobInfo job = await printer.PrintAsync(payload, options: null, cancellationToken).ConfigureAwait(false);
```

`OpenAsync` throws `NotSupportedException` for an identity form, because such an identifier
names no endpoint. Resolve it through `IPrinterManager` first.

`PrintOptions.OnUnsupported` says what to do with an option the printer does not support:

- `Send` (the default) — send the option and let the printer decide. This costs no extra
  request.
- `Throw` — read the configuration first, then throw `NotSupportedException`.
- `Drop` — read the configuration first, remove the option, and name it in
  `PrintJobInfo.DroppedOptions`.

`Throw` and `Drop` need a known configuration to compare against, so both act as `Send`
when the printer reports an empty one. An explicit `false` is a capability statement, and
both judge against it.

### Disposal

**A printer from `PrinterFactory` owns nothing and needs no disposal.** The factory keeps
one `HttpClient` and one of each status client, and shares them with every printer it
returns. Dispose the factory instead; the dependency injection registration does this at
shutdown, and afterwards both `Open` methods throw `ObjectDisposedException`.

**A directly constructed `IppPrinter` still owns its client.** `IPrinter` does not extend
`IDisposable`, so nothing reminds you. Check for `IDisposable` and dispose it when present.

The model types are read-only after construction, and the manager hands out its cached
instances, so a caller cannot change what other callers see.

## Document formats and raw printer languages

A payload keeps its `ContentType` end to end over a raw TCP channel, but an IPP server
reads the `document-format` attribute and may convert the job. The four printer command
languages — `Zpl`, `Epl`, `Cpcl` and `EscPos` — are not formats an IPP server knows, so the
library chooses the format it sends for them. Every other content type is sent unchanged.

Two wrong choices are possible, and the library avoids both:

- **The language itself.** CUPS answers `client-error-document-format-not-supported` and
  the job never prints.
- **`application/octet-stream`.** CUPS accepts it and then *re-types* the job by reading
  the bytes. ZPL, EPL and CPCL are printable ASCII, so CUPS calls them `text/plain` and
  prints the command source as text on the label. The job reports success while the output
  is wrong.

`application/vnd.cups-raw` is the only format that turns the conversion off, and only CUPS
offers it. So the choice depends on the peer:

| Peer | Format sent for a printer language |
| --- | --- |
| The local CUPS daemon (`SpoolerPrinter` on Linux and macOS) | `application/vnd.cups-raw` |
| A network printer that lists the language itself | the language, unchanged |
| A network peer that offers `application/vnd.cups-raw` (a CUPS server) | `application/vnd.cups-raw` |
| Any other network printer | `application/octet-stream` |

`IppPrinter` reads `document-format-supported` **only** for a printer-language payload, from
the cached `GetConfigurationAsync` result, so a PDF or a PNG job costs no extra request and
repeated raw jobs share one read. `CupsSpoolerDriver` needs no negotiation, because its peer
is CUPS by construction, and the Windows spooler submits with the `RAW` datatype, which
already passes the bytes through unchanged. When a printer still rejects the format, the
error names it.

## Transports

`IPrinterTransport` transmits payloads. `TcpPrinterTransport` sends to a
`NetworkPrinterEndpoint` over TCP and throws `NotSupportedException` for other endpoints.
Its timeout defaults to five seconds and applies to the connection, and then again to the
write, so a printer that accepts the connection but stops reading fails with
`TimeoutException` instead of blocking the caller. After the write the transport closes its
side, which tells the printer that the job is complete.

## Printer status over IPP

`IppPrinterStatusClient` reads identity and status with IPP Get-Printer-Attributes, and
returns make and model, state, state reasons and the supply markers. All operations are
read-only. The wire format is handled by `SharpIppNext`; see
[Packages](packages.md#runtime-dependencies).

- It tries IPPS (TLS) first and falls back to plain IPP, across `/ipp/print` and
  `/ipp/port1`. The optional `resourcePath` parameter is tried before those well-known
  paths: pass the `rp` attribute of a DNS-SD TXT record to reach a printer that serves IPP
  elsewhere.
- The default constructor owns an `HttpClient` and disposes it. A client built from a
  caller-supplied `HttpClient` does not.
- Neither this client nor `SnmpPrinterStatusClient` implements an interface, so a test
  cannot mock either one. Wrap the class behind your own seam when a test must replace it.

```csharp
IppPrinterStatusClient client = new();
IppPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken).ConfigureAwait(false);
```

## IPP transport policy

Every IPP connection this library opens follows one `IppTransportOptions`, whichever type
opened it. The connection opens over IPPS (TLS) first, and the plain IPP endpoints are
tried next when no IPPS endpoint answers.

- `AllowPlainIpp` (default `true`): many label printers speak plain IPP only, so the
  fallback is on. Set it to `false` to talk to IPPS printers only.
- `ServerCertificateValidation` (default `null`): the default accepts every certificate,
  because network printers use self-signed certificates in nearly every case. That default
  protects the print data against a passive observer only. It does not prove that the host
  is the printer you expect.
- `ConnectTimeout` (default five seconds): without it, the operating system default
  applies, which can be minutes.

Set `ServerCertificateValidation` when the application must know that it talks to the right
printer. A printer has no certificate chain to a public root, so pin its thumbprint:

```csharp
IppTransportOptions options = new()
{
    ServerCertificateValidation = (_, certificate, _, _) =>
        certificate is not null &&
        String.Equals(certificate.GetCertHashString(), knownThumbprint, StringComparison.OrdinalIgnoreCase),
};
using PrinterFactory factory = new(options);
```

**A validating client never falls back to plain IPP.** When `ServerCertificateValidation`
is set, or when you pass your own `HttpClient`, a failed TLS handshake throws
`AuthenticationException`, because clear text would defeat the trust you asked for. With
the default policy, a failed handshake means "this port speaks plain IPP", and the fallback
runs.

To share one client between several types, build it once with
`IppHttpClientFactory.Create(options)` and pass the client together with the same options to
each constructor. That factory sets the connect timeout, turns off redirects (every IPP
operation is a POST that carries the document), and installs the certificate policy. The
caller owns that client.

## Job queues and progress

`IPrintJobQueue` inspects and manages the jobs of a printer. `CompositePrintJobQueue`
routes a `spooler` identifier to the operating system spooler and a network identifier to
IPP.

`IPrintJobMonitor.WatchJobAsync` yields a reading each time the state or the progress of one
job changes, until the job reaches a terminal state or leaves the queue.
`PollingPrintJobMonitor` reads the queue again and again, so it works with every printer and
needs no notification channel.

```csharp
IPrintJobMonitor monitor = new PollingPrintJobMonitor(queue);
await foreach (var reading in monitor.WatchJobAsync(job.PrinterId, job.JobId, new PrintJobMonitorOptions(), cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

## Discovery

- `IMdnsPrinterDiscovery` — asks the local link for printers that advertise themselves.
  Prefer this: it needs no host list and opens no connection.
- `INetworkPrinterDiscovery` — probes explicit hosts for an open raw print channel. Probing
  is opt-in, and a host listed more than one time is probed one time. Use it for printers
  that do not advertise themselves.
- `IPrinterDiscovery` — enumerates the printers installed in the operating system spooler
  (Win32 print queues, CUPS destinations).

```csharp
INetworkPrinterDiscovery discovery = new TcpNetworkPrinterDiscovery();
NetworkPrinterDiscoveryOptions options = new()
{
    Hosts = ["192.168.1.50", "192.168.1.51"],
    ConnectTimeout = TimeSpan.FromSeconds(1),
};
IReadOnlyList<DiscoveredPrinter> printers =
    await discovery.DiscoverAsync(options, cancellationToken).ConfigureAwait(false);
```

To sweep the subnet the machine is already on, `NetworkPrinterDiscoveryOptions.LocalSubnetHosts`
builds the list. It reads the network adapters only, and opens no connection:

```csharp
NetworkPrinterDiscoveryOptions options = new() { Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts() };
```

An adapter counts only when it is up, has an IPv4 gateway, and has a prefix length from 16
to 30. A loopback address and a link-local address are skipped, and the list stops at
`maxHosts`, which is 4096 by default. A prefix shorter than 16 holds too many addresses to
probe, and IPv6 is not listed because a subnet there is too large to walk. The probe stays
opt-in: the method gives you the list, and you decide to use it.

### Discovery over mDNS

`MdnsPrinterDiscovery` sends one multicast DNS query to the local link and collects the
answers for `BrowseTimeout`. It replaces a subnet sweep: no host list, no connection to any
address. It opens one socket for each interface and address family, so a query goes out one
time on each link. `MdnsPrinterDiscoveryOptions` controls the service types, the timeout,
the retries, the interfaces, IPv6 and the record limit.

The TXT attributes fill `PrinterInfo`: `ty` → `Name`, `note` → `Location`, `pdl` →
`DriverName`, `UUID` → `Uuid`, `usb_MFG` and `usb_MDL` → `Manufacturer` and `Model`. A key
that appears twice keeps its first value (RFC 6763 §6.4). An address record that points to
a loopback, unspecified or multicast address is ignored, and the endpoint then keeps the
target name of the service.

| Service type | Port | Reported as |
| --- | --- | --- |
| `_pdl-datastream._tcp` | 9100 | `raw://…` |
| `_ipp._tcp` | 631 | `ipp://…` |
| `_ipps._tcp` | 631 | `ipps://…` |
| `_printer._tcp` | 515 | **nothing** |

**A printer that advertises several protocols gives one channel for each.** The channels
share a device key, so `IPrinterManager` puts them on one `PrinterDevice`. A caller needs
both: only the raw channel sends a payload unchanged, and only the IPP channel has a job
queue.

**LPD is not reported.** No transport in this library writes the LPD message format, so an
endpoint on port 515 is not a channel a job can take. A printer that advertises only LPD is
therefore out of reach and is not reported at all.

**The UUID is shared across the services of one instance.** A printer answers the `UUID`
query on its IPP service and usually not on its raw one, so the value found on any service
of an instance is applied to all of them. Without that the raw channel would never join the
device it belongs to. When a UUID is known, every channel takes the identity form and the
address stays as an alias, so an identifier a caller already holds keeps resolving.

The DNS wire format is handled by `Makaretu.Dns.New`; see
[Packages](packages.md#runtime-dependencies). The socket binds an ephemeral port, not 5353,
so the browse never competes with a responder such as avahi or Bonjour: RFC 6762 §5.1 and
§6.7 require the printer to answer such a "one-shot" querier by unicast. A strict local
firewall can discard that unicast answer, and the browse then returns nothing even though
`avahi-browse` works.

## Spooler

`SpoolerPrinterDiscovery`, `SpoolerPrintJobQueue` and `SpoolerPrinter` reach the operating
system spooler through one of two drivers, chosen at run time:

- `CupsSpoolerDriver` — Linux and macOS. CUPS runs its own IPP server on `localhost:631`,
  so this driver sends the same IPP requests, aimed at the local daemon. It needs no native
  interop.
- `WindowsSpoolerDriver` — Windows, through `winspool.drv`. Windows has no local IPP
  server, so this driver is the one part of the library that calls native code.

The Windows driver builds a `DEVMODE` for the job with `DocumentProperties` and carries it
onto the job through `PRINTER_DEFAULTS`. Two rules decide what a field can hold:

- `MediaSize` and `MediaSource` are names, and a `DEVMODE` field holds a number, so the name
  is looked up in the media and tray lists the queue reports. A name that the queue did not
  report has no number, and is dropped. The lists cost two spooler calls each, so they are
  read only when the job names a size or a tray.
- `dmPrintQuality` holds either a resolution in dots per inch or a `DMRES_*` quality name. A
  job that sets both keeps the resolution, because it is the exact number the caller gave.

`Copies` is applied by printing the document one time for each copy, because a queue with
the `RAW` data type never reads `dmCopies`. Each copy is a separate spooler job and the
returned `PrintJobInfo` names the first of them, so a failure on a later copy leaves the
earlier copies in the queue — which is what a paper jam also does.

`MediaType`, `OutputBin`, `PageRanges` and `NumberUp` have no `DEVMODE` field, so the driver
always reports them in `PrintJobInfo.DroppedOptions`, whatever `OnUnsupported` says. Two
more options are carried only in part. `dmScale` is a percentage and not a fit mode, so
`Scaling` reaches it as `PrintScaling.None`, which is 100 per cent, and `Auto`, `AutoFit`,
`Fill` and `Fit` are dropped. `dmOrientation` holds `DMORIENT_PORTRAIT` and
`DMORIENT_LANDSCAPE` and nothing else, so an `Orientation` of `ReverseLandscape` or
`ReversePortrait` is dropped as well. IPP and CUPS carry all of them. The
defaults on `PrinterConfiguration` come from the same device mode.
`WindowsSpoolerDeviceModeMapper` holds the whole name-to-number mapping and calls no native
code, so the unit tests on Linux prove it.

A `SpoolerPrinterEndpoint` name has at most 127 characters and contains no control character
and none of `/`, `?` and `#`, which would end the path segment the CUPS driver builds from
it. Spaces and a Windows connection name such as `\\server\queue` are accepted, and two
names are equal without regard to case, as Windows and CUPS compare them.

Native Windows calls cannot run in this repository's test suite or CI, which both run on
Linux. [Windows manual tests](windows-manual-tests.md) lists what a person must check by
hand.

## Printer status over SNMP

`SnmpPrinterStatusClient` reads the Printer MIB (RFC 3805) and the Host Resources MIB from a
host you already know. Many printers supply these and do not answer IPP, and SNMP also
reports the serial number and the page count, which IPP does not carry. All operations are
read-only.

```csharp
SnmpPrinterStatusClient client = new();
SnmpPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken).ConfigureAwait(false);
Console.WriteLine($"{details.Info.Name}: {details.Status.SerialNumber}, {details.Status.LifetimePageCount} pages");
```

- `SnmpPrinterStatusOptions` sets the community, the request timeout and the retry count
  (two, which gives three attempts, because UDP can lose a datagram). The client throws
  `InvalidOperationException` when no attempt is answered. A datagram from another address,
  or a malformed datagram, is not an answer and is discarded.
- A host name that resolves to both address families is queried over IPv4, because most
  printers answer SNMP on IPv4 only.
- When the agent answers the supply walk with `tooBig`, the client asks again one time for
  half as many rows. Every other SNMP error status is an `InvalidOperationException`.
- `hrPrinterStatus` gives the state. The bits of `hrPrinterDetectedErrorState` can raise it
  to `Error` or `Offline`, and every set bit is named in `PrinterStatus.Detail`. A bit that
  is only a warning, such as `lowToner`, does not change the state, because a printer low on
  toner still prints.
- The Printer MIB uses a negative supply level for a value that is not a quantity: `-1` is
  "other", `-2` is "unknown amount remains" and `-3` is "some amount remains". For a
  negative level, `PrinterMarker.LevelPercent` is `null` and `LevelRaw` keeps the reported
  number.
- **This client does not search a network.** Find printers with a discovery first.
- Only SNMP version 2c is supported.

The messages are encoded by `Lextm.SharpSnmpLib`, but the datagrams travel over our own UDP
channel, which keeps the timeout and the retry behaviour under our control. That library is
taken at a prerelease version on purpose; see
[Packages](packages.md#approved-exception-a-prerelease-snmp-dependency).

## Printer manager

`IPrinterManager` gives one entry point for printing. `DiscoverAsync` finds printers and
`PrintAsync` prints to one of them.

**`DiscoverAsync` returns devices, not channels.** One physical printer is one
`PrinterDevice`, however many ways it can be reached. Its channels are on `Channels`,
ordered the way the manager prefers them, and grouped on `ChannelsByTransport`.

```csharp
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();
var devices = await manager.DiscoverAsync(null, cancellationToken).ConfigureAwait(false);
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

### The three phases

1. **Discover.** Every configured source runs at once and reports the channels it found.
2. **Enrich.** When the caller asked for it, each channel is opened once and asked what it
   supports and which device it belongs to. This phase is opt-in, because every answer costs
   a request.
3. **Group.** The channels are put into devices.

### Which sources run

`IncludeMdns` and `IncludeSpooler` default to `true`. **The TCP probe never runs on its
own.** It needs a host list, so it runs only when `Probe` carries one. To change how long
the browse waits, set `Mdns.BrowseTimeout`; the timeout is not on `PrinterManagerOptions`
itself.

**All three sources can run, or none of them.** When every source is off, `DiscoverAsync`
returns an empty list. It does not throw. No source ran, so no source failed.

**`DiscoverAsync` throws `PrinterDiscoveryException` only when every source failed.**
`Failures` holds the error of each source. A source that failed while another source
answered is not reported.

### Reading capabilities and identity

Both reads are off by default and each costs one request per channel.

- **`ReadCapabilities`** fills `DiscoveredPrinter.Configuration`. When it is off, the
  property stays `null`, which means "not read".
- **`ReadIdentity`** asks each channel which device it belongs to. This is what merges the
  channels of one printer when the browse alone reported no identity: an IPP read for
  `printer-uuid` and `printer-device-id`, an SNMP read for the serial number, and a spooler
  read for the device URI of a queue.
- **`MaxEnrichmentConcurrency`** bounds how many channels are read at once. It defaults to
  eight, because a probe of a whole subnet can return hundreds of channels.

**A channel that does not answer never fails the discovery.** It is reported exactly as the
browse found it, with no capabilities and no identity.

```csharp
var devices = await manager.DiscoverAsync(
    new PrinterManagerOptions { ReadIdentity = true, ReadCapabilities = true },
    cancellationToken).ConfigureAwait(false);
```

### Which print options a channel applies

`DiscoveredPrinter.SupportedOptions` names the `PrintOptions` properties that channel
applies. Every other option is dropped or ignored, whatever the caller sets. The value comes
from the transport, which the library knows without asking anything, and is narrowed by the
capabilities when those were read.

| Channel | Applies |
| --- | --- |
| `raw` | Nothing. The payload reaches the device unchanged. |
| `spooler` on Windows | `JobName`, `Copies`, `Duplex`, `ColorMode`, `Orientation`, `MediaSource`, `MediaSize`, `ResolutionDpi` and `Quality`. The rest have no device mode field and are reported in `PrintJobInfo.DroppedOptions`. |
| `spooler` on CUPS, `ipp`, `ipps` | Everything the library models, narrowed by what the printer reported. |

A capability the printer did not report is not one it denied, so only an explicit `false`
narrows the set.

### How channels are grouped into devices

Two channels are put on one device when a source **vouched** that they reach the same
device, or when they share an address. Vouching is evidence, never resemblance:

| Source | Evidence |
| --- | --- |
| mDNS | the `UUID` TXT record |
| IPP | `printer-uuid`, and the `SN` field of `printer-device-id` |
| SNMP | `prtGeneralSerialNumber` |
| CUPS | `device-uri` — a host, a USB serial, or a UUID |
| Windows spooler | a port name that is an address, such as `IP_192.168.1.5` |
| TCP probe | none: a socket that accepted a connection says nothing about identity |

The device key is the strongest one in the set: a UUID, then a serial number, then a host,
then a queue name. A device that reported an identity therefore keeps its key when its
address changes.

**The library refuses to merge on anything else.** In particular:

- Two different hosts with no vouching alias stay two devices, **even with the same name,
  model and location**. Two identical printers on a shelf are two printers.
- A queue and a host stay apart with no `device-uri` or port name to link them, even when
  the two strings are equal.
- A loopback or unspecified address in an alias is ignored, or every CUPS queue on one
  machine would fuse into one device.
- A placeholder identity is ignored: a blank value, the empty UUID, `n/a`, `none`, `0`,
  `unknown`, a bare `SN:`, anything under three characters, or a value made of one repeated
  character. A whole fleet shipped with the same placeholder serial number would otherwise
  collapse into a single device.
- The CUPS `printer-uuid` is **never** used. CUPS mints it itself, as a hash over the
  server, the port and the queue name, so two queues to one printer report two different
  values. Only `device-uri` links a queue to its device.

**Grouping by address assumes a plain local network.** Behind a print server or a network
address translation, two devices can share a host, and the library cannot tell. Set
`ReadIdentity` where that matters.

### Choosing a channel for a call

The channel named by the identifier is used when it does what the call needs. Otherwise
another channel of the same device is used, which is a real gain: a printer found through
the spooler can be printed to over its raw channel. Every call prefers a channel with a job
queue, so the job can be watched after it is sent. The order is `ipps`, `ipp`, `spooler`,
`raw`.

`RequirePassthrough` picks a channel that sends the payload bytes to the device unchanged,
and throws `NotSupportedException` when the device has no such channel:

| Channel | Keeps the promise |
| --- | --- |
| `raw` | Yes |
| `spooler` on Windows (`RAW` data type) | Yes |
| `spooler` on Linux and macOS (CUPS) | No |
| `ipp`, `ipps` | No |

**CUPS is refused even though the library sends `application/vnd.cups-raw`.** That format
stops CUPS from re-typing the job, which is necessary but not sufficient. A queue with a
driver, and a driverless (IPP Everywhere) queue, still convert the job into a format the
device reads. Only a CUPS *raw* queue passes the bytes to the backend untouched, and CUPS
reports no dependable attribute that tells a raw queue apart, so the library does not
promise what it cannot verify. A caller who knows the queue is raw should print **without**
`RequirePassthrough`: the document format is correct either way, and the property only
controls whether the manager makes a guarantee first.

```csharp
var label = devices.First(d => d.Details.Name.Contains("Zebra", StringComparison.Ordinal));

PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
await manager.PrintAsync(
    label.Id,
    payload,
    new PrintOptions { RequirePassthrough = true },
    cancellationToken).ConfigureAwait(false);
```

### Resolving an identifier

- **An address form is opened directly.** It names its own endpoint, so no browse and no
  probe runs. A host that answers nothing is no longer an addressing problem the manager
  reports: the transport error is the answer, and it names the address.
- **An identity form runs one fresh discovery**, because a UUID names no address. The call
  throws `InvalidOperationException` only when the identifier is still unknown after that.
- **Every key of a device resolves to it**: its own key and every alias a source vouched
  for. An identifier kept from before an identity was read therefore keeps working.
- **A stale entry does not repair itself.** The manager does not re-discover after a failed
  print, because most print failures are not addressing problems: no paper, no permission, a
  rejected option. Call `DiscoverAsync` to refresh.
- **`DiscoverAsync` does not remove cache entries.** A printer removed from the network
  stays in the cache, and every print to it keeps failing.

### Status and job progress

`GetStatusAsync` resolves the identifier the same way `PrintAsync` does, and opens a channel
a status can be read from.

**`WatchJobAsync` checks the device for a job queue before it watches anything.** A device
with an `ipp`, `ipps` or `spooler` channel has one. A device with only a raw channel does
not, and the call throws `NotSupportedException`.

This refusal is correct, not a limitation. A raw channel gives back a generated job
identifier and has no queue to read. Watching such a job through IPP would ask the wrong
protocol about a job it never saw, and a past defect showed the cost: the empty answer read
as "the job is done", so the caller was told the label was finished before the printer did
any work.

```csharp
await foreach (var reading in manager.WatchJobAsync(
    label.Id, job.JobId, new PrintJobMonitorOptions(), cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

## Dependency injection

`AdaptArch.Devices.DependencyInjection` provides `AddDevices()` and `AddPrinters()`.
`AddPrinters()` registers every printer type as a singleton. Most hold no state between
calls. Three hold state that is safe to share: `CompositePrintJobQueue` keeps one queue per
network host in a thread-safe dictionary, `SnmpPrinterStatusClient` keeps a request counter
it updates atomically, and `PrinterManager` keeps the device cache and a semaphore that
stops several callers from all running a discovery at once. That cache is why
`PrinterManager` must stay a singleton.

Every registration uses `TryAdd`, so a registration the application made first is left in
place. The optional callback sets the `IppTransportOptions` for every IPP type the container
serves. One `HttpClient` is built from those options, is shared by the factory, the status
client and the job queue, and is disposed with the container:

```csharp
services.AddPrinters(options => options.AllowPlainIpp = false);
```

## Sample

`samples/Devices.Samples` has five scenarios:

- `interactive` — a menu, and the default when no argument is given. It discovers once, then
  lets you list the printers, print a file through a queue (spooler or IPP) and watch the
  job, print a file raw through a passthrough or spooler channel, and read status and
  capabilities. Every selection is a number, so no identifier is copied by hand. The queue
  flow asks for a colour mode for an image, and for a rotation and a scaling mode when the
  printer reported which ones it accepts. The raw flow lists the spooler beside the raw
  channel, and says which of the two promises to keep the bytes.
- `test-run` — one scripted hardware test, also menu entry 6. It discovers, prints the
  capabilities and the status of every printer, then sends five jobs through one spooler
  queue: a PDF with the defaults, a PNG in colour at its own size, a PNG in grayscale
  rotated 90 degrees, a JPEG rotated 180 degrees, and a JPEG in grayscale filling the media.
  It then asks again, and sends a JPEG, a ZPL label and an EPL label raw to every
  raw-capable channel. A format the channel says it does not read is skipped with a yellow
  warning, because a raw send is not converted and would only waste paper. The jobs never
  change, so two runs can be compared. An option the printer did not report is still sent,
  and the run says so first.
- `print-manager` — `IPrinterManager` for discovery, status, sending, watching and ZPL. This
  is the layer most callers want.
- `manual-management` — `IMdnsPrinterDiscovery`, `INetworkPrinterDiscovery` and
  `IPrinterTransport` directly. This shows the seams the manager sits on. Its `send` command
  writes raw bytes over a network channel, so it cannot reach a spooler queue.
- `win-printer-test` — reads a Windows print queue and prints the evidence for the
  [Windows manual tests](windows-manual-tests.md). It needs Windows.

The commands take a printer identifier, not a bare host, and `discover` prints the exact
identifier of each channel it finds. A value that is not a URI is read as a raw network
address, so a plain IP address still works. Run `dotnet run --` with no arguments for the
command list.

### Can a raw send print a PDF?

Only if the printer firmware contains a PDF interpreter. A raw channel writes the bytes to
TCP port 9100 without a change, and nothing on the way converts them. A printer without a
PDF interpreter prints nothing, or prints the PDF source as text. The same rule applies to
PNG and to a label language.

**Ask the channel, not the device.** The channels of one printer read different formats,
and `document-format-supported` describes the IPP service alone. The `pdl` key of the
DNS-SD advertisement, which the library keeps in `PrinterInfo.DriverName`, names what one
channel reads, so it is the first place to look. An EPSON L6270 answers like this:

```text
ipp channel  pdl = application/octet-stream, image/pwg-raster, image/urf, image/jpeg,
                   application/vnd.epson.escpr
raw channel  pdl = application/vnd.epson.escpr
```

The IPP service takes a JPEG; TCP port 9100 takes ESC/P-R and nothing else. Judging that
printer by its device-wide format list would promise a raw JPEG print that cannot work.

`PrinterDevice.Accepts` answers the question before you send:

```csharp
var accepts = device.Accepts(channel, PrinterContentTypes.Pdf);
// true   the channel reports the content type
// false  the channel reports other content types only
// null   nothing was reported, which is not a refusal
```

It reads three sources in order, and stops at the first one that answered:
`PrinterInfo.DriverName` for the channel, which holds the `pdl` record; then
`PrinterConfiguration.SupportedDocumentFormats`, which comes from IPP
`document-format-supported`; then `PrinterDeviceDetails.CommandSets`, which comes from the
IEEE 1284 `CMD` field and belongs to the whole device.

Two rules are built in. A CUPS queue names no printer language of its own and takes one as
`application/vnd.cups-raw`, so a queue that lists that format carries a ZPL or an EPL
label. And `application/octet-stream` is not read as an answer: nearly every channel lists
it, and CUPS re-types such a job as `text/plain`, which prints the command source instead
of the label.

The `interactive` scenario calls it and warns before it wastes paper. A printer that
reports nothing did not refuse; it only did not answer.

To print a PDF on a printer that has no PDF interpreter, send it through the spooler queue
instead. The queue driver rasterises the document.

### Building the sample with trimming and native AOT

```bash
sh ./pipeline/publish-samples.sh -r linux-x64
```

The script publishes the sample three times — framework-dependent, trimmed self-contained,
and native AOT — into `./artifacts/samples/<rid>/`. Warnings stay errors, so an `IL2xxx` trim
warning or an `IL3xxx` AOT warning fails the publish. This is how a trim problem in the
library is found early. Use `-o` to select another output directory.

## USB printers

**This library does not talk to a USB printer directly, and does not intend to.** There is
no USB transport, no USB endpoint and no `usb` scheme. `PrinterId.TryParse` returns `false`
for a `usb://…` text. Print to a USB printer through the operating system queue, with a
`spooler://` identifier: the queue already owns the device, `SpoolerPrinter` already prints
to it on all three platforms, and `SpoolerPrinterDiscovery` finds it.

| System | What owns the device | What direct access costs |
| --- | --- | --- |
| Windows | `usbprint.sys` | `libusb` needs that driver replaced by WinUSB or libusbK, which breaks normal printing for every other application. |
| Linux | the `usblp` module, as `/dev/usb/lp0` | `libusb` must detach the kernel driver, which removes the device node and makes CUPS fail. It also needs udev rules. |
| macOS | the CUPS `usb` backend | There is no device node. Direct access needs IOKit interop and takes the device away from CUPS. |

**Grouping still works.** A CUPS queue reports `device-uri` as
`usb://Zebra/ZTC%20ZD421?serial=X4TY012345`, and `DeviceUriParser` reads the serial number
out of it. That serial becomes the same device key an IPP or SNMP read produces, so a queue
and the network channels of one printer still merge into one `PrinterDevice`.

A device that is reachable only over USB and is **not** installed in the print queue is out
of reach of this library. Install it in the queue first.

## Not yet implemented

- An LPD transport. `_printer._tcp` answers are discovered and then discarded.
- SNMP version 3, which adds authentication and privacy.
- IPP notifications (`Create-Printer-Subscriptions`, `Get-Notifications`).
