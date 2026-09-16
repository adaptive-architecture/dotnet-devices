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
ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242     the identity form, port 631 implied
ipps://e3b0c442-98fc-1c14-9afb-4c8996fb9242:443  an identity on a port that is not the default
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
- **An identity form carries the port too**, by the same rule: written when it is not the
  default of the scheme, left out when it is. Once a device has named itself, the port is
  all that tells two of its channels apart, and without it a printer that answers one
  scheme on two ports would report one identifier twice. It still takes no part in
  `DeviceKey`, so those channels stay one device.
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

## Add a format the library does not know

Every content type the library knows is a `PrinterFormat` in a `PrintFormatPolicy`, and an
application registers its own the same way. A format has a kind, which states what a printer
does with the bytes:

| Kind | What it means | Where it goes |
| --- | --- | --- |
| `RawLanguage` | Commands the printer firmware reads | A channel that sends the bytes unchanged. CUPS is told to apply no filter |
| `Image` | A raster the driver draws | The GDI page on Windows, the queue elsewhere |
| `Document` | Pages a converter turns into images | The converter first, then the image path |
| `Opaque` | Anything else, and the kind of every unregistered type | A channel that sends the bytes unchanged |

A content type that is registered nowhere still prints. It is `Opaque`, so it travels
unchanged, which is what an unknown vendor stream needs.

```csharp
services.AddPrinters(configureManager: options =>
{
    // A label language the library does not know. "STAR" is the token the printer
    // reports in its IEEE 1284 command set.
    options.Formats.Add(new PrinterFormat("application/vnd.star-line", PrinterFormatKind.RawLanguage, "STAR"));

    // A document format, with the converter that prints it.
    options.Formats.Add(new PrinterFormat("image/tiff", PrinterFormatKind.Document));
    options.Converters.Add(new TiffConverter());
});
```

A converter turns one payload into one image per page:

```csharp
public sealed class TiffConverter : IPrintPayloadConverter
{
    public bool CanConvert(string contentType) =>
        String.Equals(contentType, "image/tiff", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<byte[]>> ConvertAsync(
        byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        // context.Dpi is what the job asked for, or 300. Clamp it to what the engine
        // renders well. PageRange.Select turns
        // context.PageRanges into the zero-based pages to keep.
        var pages = PageRange.Select(PageCountOf(data), context.PageRanges);
        return await RenderPngPagesAsync(data, pages, context.Dpi, cancellationToken);
    }
}
```

The converter runs before the job reaches the spooler, so a file it refuses spools nothing.
The resolution is passed on as the caller asked for it, because a band that suits one engine
is not a rule for another: each converter clamps to what it renders well.
A converter returns pages in `context.TargetContentType`, which is `image/png` today.

`PrinterManagerOptions.Converters` scopes a converter to one manager.
`PrintFormatPolicy.AddDefaultConverter` registers one for the whole process, which is what
an application without a manager needs, and what
`WindowsPrinting.EnableSpoolerPdfPrinting()` calls. A converter on the manager wins over a
process one for the same format, so an application can replace the built-in behaviour.

What the registration changes:

- **Routing.** `PrinterManager` sends a `RawLanguage` payload to a channel that keeps the
  bytes, exactly as it does for ZPL.
- **The format sent over IPP.** A registered language is protected with
  `application/vnd.cups-raw`, so CUPS does not re-type it as text.
- **`PrinterDevice.Accepts`.** The `CommandSet` of a format is the IEEE 1284 token matched
  against what the printer reported.
- **The Windows spooler path.** The kind decides whether the job is drawn with GDI,
  converted first, or passed through as `RAW`.

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

`PrinterStatus` reports the state reasons three ways, and each one has a purpose:

- `StateReasons` holds one entry for each reason. **Match this one**, for example
  `cups-pki-expired`. A printer with no reason gives an empty list: the protocol keyword
  `none` means "no reason at all", so it is never an entry.
- `Detail` holds the same list joined with `"; "`, for a person to read. It kept its exact
  shape when `StateReasons` was added, so no caller broke.
- `StateMessage` and `DetailedStatusMessages` hold `printer-state-message` and
  `printer-detailed-status-messages`: free text the printer wrote. Do not parse either one.

`PrintJobInfo` carries the same four fields for a job, plus `PrinterStateMessage`, which is
the `job-printer-state-message` attribute. **CUPS puts the text of its own log there**, so it
is usually the only field that names why a job stopped.
[Troubleshooting](troubleshooting.md) works through that case.

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

## Diagnostics

A job that will not print is the case the library is measured on.
[Troubleshooting](troubleshooting.md) is the guide; this section names the parts.

**The transport that answered.** `IppPrinter.Connection` and `IppPrintJobQueue.Connection`
report the scheme and the endpoint that answered, and `PrinterStatus.Connection` carries the
same. A downgrade from IPPS to plain IPP is then visible. Each one is `null` until the first
call finds an endpoint, and a transport failure clears it. A local CUPS queue always reports
`Ipp`, because the server listens on the IPP socket of the machine and no TLS attempt is
made there, so read this for a network printer and not for a `spooler://` one.

**Failures carry data, not only text.**

- `PrinterConnectionException.Failures` holds the cause of **each** endpoint that was tried,
  and `InnerException` is the **first** one. The order matters: the probe tries up to six
  endpoints, and a TLS handshake that failed over IPPS says more than the "connection
  refused" of a plain IPP port tried later. Keeping only the last cause reported the wrong
  one. Read `InnerException` for that first cause; `Failures` is a dictionary and promises
  no order.
- `PrinterOperationException` carries `PrinterId`, `Endpoint`, `Operation` and
  `IppStatusCode`, the status code of RFC 8011 section 13.1. A caller matches the code
  instead of the message.
- Both derive from `InvalidOperationException`, which this API documented before, so an
  existing `catch` block still catches them.

**The log.** Register an `ILoggerFactory` and let `AddPrinters()` take it, or set
`PrinterManagerOptions.LoggerFactory` and `IppTransportOptions.LoggerFactory` by hand. The
library writes in four categories that nest under `AdaptArch.Devices.Printing`, so one filter
rule turns on the manager, the IPP wire, the discovery sources and the Windows spooler
together.

The level says what the library did about a failure: `Error` means it swallowed one and gave
you a result anyway, `Warning` means it continued with less than you asked for, `Information`
marks a milestone that is safe to leave on, `Debug` is one entry for each operation, and
`Trace` is one for each item. **A failure that reaches your code as an exception stays at
`Debug`**, because the exception already carries it. A job name and a user name are personal
data, so they are at `Debug` and below only.

[Troubleshooting](troubleshooting.md) lists every event with its identifier, which is stable
across versions so a report can name one. An application that sets no factory writes nothing
and pays almost nothing.

**The raw answer.** Set `IppTransportOptions.CaptureRawResponses` to read what the printer
sent, including the attributes the library does not map. The attributes reach
`PrinterStatus.RawAttributes`, `PrintJobInfo.RawAttributes` and
`PrinterOperationException.RawAttributes` as `IppAttributeSnapshot` records, which carry text
only: no `SharpIppNext` type is in the public API. Keep the switch off in normal operation.

SNMP and the Windows spooler fill `StateReasons` beside `Detail`, and both write a log.
Neither reports a state message or raw attributes. The CUPS spooler speaks IPP, so it reports
everything above: `SpoolerPrinter`, `SpoolerPrinterDiscovery` and `SpoolerPrintJobQueue` each
carry an `IppTransport` property that holds the same `IppTransportOptions`, and
`AddPrinters()` gives them the registered one.

## Job queues and progress

`IPrintJobQueue` inspects and manages the jobs of a printer. `CompositePrintJobQueue`
routes a `spooler` identifier to the operating system spooler and a network identifier to
IPP.

The manager can also read a queue as evidence of which device a channel belongs to. See
[Correlating channels by their job queue](#correlating-channels-by-their-job-queue).

**The IPP queue states which attributes it wants.** A printer left to its own default
answers Get-Jobs with the job identifier alone, and none of the messages that say why a job
stopped. Both `GetJobsAsync` and `GetJobAsync` therefore send an explicit
`requested-attributes` list that holds every attribute the mapper reads.

`IPrintJobMonitor.WatchJobAsync` yields a reading each time the state or the progress of one
job changes, until the job reaches a terminal state or leaves the queue.
`PollingPrintJobMonitor` reads the queue again and again, so it works with every printer and
needs no notification channel.

```csharp
IPrintJobMonitor monitor = new PollingPrintJobMonitor(queue);
PrintJobMonitorOptions watch = new() { IdleTimeout = TimeSpan.FromMinutes(2) };
await foreach (var reading in monitor.WatchJobAsync(job.PrinterId, job.JobId, watch, cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

#### When a watch ends

**Only the `CancellationToken` throws. Every limit ends the watch quietly.** The watch stops
and the loop simply finishes; a watch whose last reading was not terminal ended early, and
that last reading is how a caller says so.

**Never pass a deadline as the cancellation token.** The monitor cannot tell a caller's
deadline from a real interruption, so it does what the token says and throws. A past defect
showed the cost: one `CancellationTokenSource(2 minutes)` covered a file read, the
submission and the watch, and a printer that woke from sleep and printed slowly took the
whole run down with an unhandled `TaskCanceledException`. The token is for a caller that
wants to stop; a limit belongs in the options.

| Limit | Ends the watch when |
| --- | --- |
| `IdleTimeout` | Neither the state nor the page count has moved for that long. |
| `Timeout` | That long has passed in total, whatever the job is doing. |

**Prefer `IdleTimeout`.** Elapsed time does not separate a slow job from a stuck one;
change does. A printer that wakes from sleep can take minutes over the first page and then
print steadily: every page resets `IdleTimeout`, so the watch survives, while `Timeout`
would end a job that is working perfectly. Set `Timeout` only as an outer bound. Both may be
set, and whichever comes first ends the watch.

**A sleeping printer needs no waking.** Submitting a job wakes it — that is what IPP and the
spooler already do — but the first page may take minutes while it warms up. During that time
the job reports `Printing` with no page finished, which is exactly the case `IdleTimeout` is
sized for.

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

| Service type | Usual port | Reported as |
| --- | --- | --- |
| `_pdl-datastream._tcp` | 9100 | `raw://…` |
| `_ipp._tcp` | 631 | `ipp://…` |
| `_ipps._tcp` | 443 or 631 | `ipps://…` |
| `_printer._tcp` | 515 | **nothing** |

**The port is the one the `SRV` record gave, never a default for the service type.** A
printer that advertises `_ipps._tcp` on 443 is reached on 443.

**`_ipp` and `_ipps` are two channels and not one.** They are separate services, on separate
ports, and a printer commonly advertises both. Collapsing them into one scheme would hide
the plain channel behind a secure one that may not negotiate — an older printer whose
certificate has expired is the usual case — and a caller could then not reach a printer that
answers perfectly well on 631.

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
onto the job. Printer languages use the `RAW` data type and pass the bytes through
unchanged; PNG and JPEG images are drawn onto a GDI printer device context with GDI+
so the driver rasterises the page, using only the system `gdi32.dll` and `gdiplus.dll`
and no extra NuGet package. PDF pages render to PNG first with the in-box Windows
engine, then print as one GDI document through the same path. That renderer lives in
the separate `AdaptArch.Devices.Windows` package (a `-windows` target is the only one
that can see the engine), and the application lights it up with
`WindowsPrinting.EnableSpoolerPdfPrinting()`; without a converter for PDF a job fails
with `NotSupportedException` before anything spools. Any other document format prints
the same way once the application registers a converter for it. It runs on Windows 10 version 1607
and later, including Windows 11, which is the floor .NET 10 itself requires; the
`gdi32`/`gdiplus` entry points it calls ship in-box on all of them. Two rules decide
what a device mode field can hold:

- `MediaSize` and `MediaSource` are names, and a `DEVMODE` field holds a number, so the name
  is looked up in the media and tray lists the queue reports. A name that the queue did not
  report has no number, and is dropped. The lists cost two spooler calls each, so they are
  read only when the job names a size or a tray.
- `dmPrintQuality` holds either a resolution in dots per inch or a `DMRES_*` quality name. A
  job that sets both keeps the resolution, because it is the exact number the caller gave.

`Copies` is applied by printing the document one time for each copy, because a queue with
the `RAW` data type never reads `dmCopies`. Each copy is a separate spooler job and the
returned `PrintJobInfo` names the first of them, so a failure on a later copy leaves the
earlier copies in the queue — which is what a paper jam also does. Image jobs are the
exception: the GDI path honours `dmCopies`, so one job prints every copy.

`MediaType`, `OutputBin`, `PageRanges` and `NumberUp` have no `DEVMODE` field, so the driver
always reports them in `PrintJobInfo.DroppedOptions`, whatever `OnUnsupported` says. Two
more options are carried only in part for printer languages. `dmScale` is a percentage
and not a fit mode, so `Scaling` reaches it as `PrintScaling.None`, which is 100 per cent,
and `Auto`, `AutoFit`, `Fill` and `Fit` are dropped. `dmOrientation` holds
`DMORIENT_PORTRAIT` and `DMORIENT_LANDSCAPE` and nothing else, so an `Orientation` of
`ReverseLandscape` or `ReversePortrait` is dropped as well. Image jobs lay out with GDI
instead and honour every orientation and every scaling mode, so neither is dropped there.
IPP and CUPS carry all of them.

A GDI image job sizes the image from the resolution its file declares — the PNG `pHYs`
chunk, the JPEG JFIF density, or an EXIF tag — and writes it at the resolution of the
device, so `PrintScaling.None` covers the same paper on a 300 and on a 600 dot printer.
A converted page is sized from the resolution the converter was asked for instead,
because the encoder writes none of its own. CUPS does the same on the Linux side, with
one difference worth knowing: a file that declares no resolution is 200 dots an inch to
CUPS, while GDI+ answers 96 for such a file and cannot tell it from one that really
declares 96. The same bare file therefore prints about half as wide on Windows. Declare
the resolution in the file to get the same page on both. The
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
  to `Error` or `Offline`, and every set bit is named in `PrinterStatus.StateReasons`, and
  joined into `PrinterStatus.Detail`. A bit that
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

### The four phases

1. **Discover.** Every configured source runs at once and reports the channels it found.
2. **Enrich.** When the caller asked for it, each channel is opened once and asked what it
   supports and which device it belongs to. This phase is opt-in, because every answer costs
   a request.
3. **Correlate.** When the caller asked for it, the IPP channels no source named a device
   for are asked what is in their queue, and the ones that answer with the same jobs are
   merged. This phase is opt-in as well, and opt-in again before it writes anything.
4. **Group.** The channels are put into devices.

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
| `spooler` on Windows | `JobName`, `Copies`, `Duplex`, `ColorMode`, `Orientation`, `MediaSource`, `MediaSize`, `ResolutionDpi` and `Quality`. Printer languages report the rest in `PrintJobInfo.DroppedOptions`; image jobs apply every `Orientation` and every `Scaling` with GDI instead of the device mode. |
| `spooler` on CUPS, `ipp`, `ipps` | Everything the library models, narrowed by what the printer reported. |

A capability the printer did not report is not one it denied, so only an explicit `false`
narrows the set.

### Correlating channels by their job queue

**A job in the queue of one channel is in the queue of another only when the two read one
queue.** That is the whole idea. It closes the case no identity read can: one printer at an
IPv4 address, at an IPv6 address and at an mDNS host name, or on two network interfaces,
with no `printer-uuid` and no usable serial number to tie them together.

It is off by default. Set `PrinterManagerOptions.QueueCorrelation` to turn it on.

```csharp
PrinterManagerOptions options = new()
{
    ReadIdentity = true,
    QueueCorrelation = new QueueCorrelationOptions(),
};
```

**Two consents, not one.** The options object itself says "you may open and read". Its
`AllowTracerJob` says "you may write". One flag would let a caller who wanted a queue read
silently acquire a job submission.

**The policy is read from the manager and never from the argument of `DiscoverAsync`**, for
the same reason `Transports` is: the argument scopes one discovery, and consent to write to
a printer is not a scope.

**It proves one queue, not one engine.** A class or a pool spreads one queue over several
devices, so a match is not a statement about which machine feeds the paper. The library
already treats a queue as a grouping unit, so this is consistent, but it is not the same
claim as "the same physical printer".

#### What a queue can and cannot prove

| Channel | Sees a job another channel put in a queue | Why |
| --- | --- | --- |
| `ipp`, `ipps` on the same queue | Yes | `Get-Jobs` returns it |
| `raw` | No | The channel has no queue |
| SNMP | No | The Printer MIB has no job table, and a held job never reaches the device |
| `spooler` | No | The job waits in that spooler and the device is never told. A queue is already tied to its device by `device-uri` or a port name, which is stronger evidence |

So this is evidence between IPP channels and nowhere else. Only `ipp` and `ipps` channels on
a transport the manager may open are candidates, and only ones no identity has already
grouped — setting `ReadIdentity` as well therefore makes the correlation cheaper and not
dearer.

#### Stage one: compare the queues that are already there

Each candidate is asked for its not-completed jobs, with one `requesting-user-name` for the
whole run, so a printer that scopes an answer by owner scopes both sides of a comparison the
same way. Nothing is written.

**A job counts as evidence only when it is distinctive.** It needs an identifier, a creation
time, and a name that is neither blank nor one every second job carries — `Document`,
`Untitled`, `Test Page`, `(stdin)`. `time-at-creation` is seconds since that printer powered
up, so a coincidence would need two devices to agree on the same identifier, the same
human-chosen name, the same owner and the same uptime to the second.

**Two queues match only when their distinctive jobs are exactly equal, as a set.** One job
in common is not enough: an overlap rule would merge a member of a class with the class
itself. A queue with no distinctive job is no answer at all, and never a match with another
queue that also had none — two idle printers are still two printers.

#### Stage two: one tracer job

When a queue held nothing distinctive, the only way to make it say something is to put
something in it. `AllowTracerJob` permits that.

**A tracer is a `Create-Job` that is never given a document.** It is not a held print job. A
job that carries no document prints nothing whatever the printer does with the hold, so the
guarantee is structural and not a gate the library checks. `job-hold-until = indefinite` is
sent as well, so the tracer does not sit at the head of the queue holding up the next job,
and a channel that does not report both `Create-Job` and an indefinite hold is never asked.
There is no fallback that sends a document.

The job name is `adaptarch-devices-correlation-` and a fresh identifier, so a job left behind
names itself to whoever finds it. **One tracer is created on every unproven channel before
any queue is read again**: each name is unique, so a single re-read reveals every pairing at
once, and the whole stage costs one write per channel rather than one per pair.

**Every tracer is cancelled in a `finally`, on a token of its own.** This is the one place in
the library that ignores the caller's cancellation: a discovery that was cancelled must still
take its job back out of the queue. A tracer whose `Create-Job` answer was lost is found
again by its name in the re-read and cancelled too.

**A tracer is never created while the manager is only refreshing to resolve an identifier.**
`PrintAsync` with an identifier the cache does not hold triggers a discovery, and printing
one label must not leave a job on every idle printer on the network.

#### What it costs

With *n* candidates: *n* reads for stage one, and for stage two one capability read, one
`Create-Job`, one re-read and one `Cancel-Job` each. Never *n²* writes.
`MaxEnrichmentConcurrency` bounds how many channels are read at once, and `MaxChannels`
(sixteen by default) refuses the whole correlation rather than correlating an arbitrary
subset, because a partial answer would depend on which channel sorted first.

**A correlation that failed proves nothing and never fails a discovery that worked.**

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
| IPP job queue | the same not-completed jobs, reported by two channels, when [queue correlation](#correlating-channels-by-their-job-queue) is on |
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
- A queue with no distinctive job is no evidence. Two idle printers that both report job
  `1` with no name stay two devices, and so do two printers that both hold a job called
  `Document`.
- The CUPS `printer-uuid` is **never** used. CUPS mints it itself, as a hash over the
  server, the port and the queue name, so two queues to one printer report two different
  values. Only `device-uri` links a queue to its device.

**Grouping by address assumes a plain local network.** Behind a print server or a network
address translation, two devices can share a host, and the library cannot tell. Set
`ReadIdentity` where that matters.

### Choosing a channel for a call

**The payload chooses the channel, not a flag.** `PrintAsync` reads
`payload.ContentType` and picks the channel that suits it. There is nothing to switch on,
because a caller who sends ZPL never wants the bytes rewritten.

The rules run in this order:

1. **A channel that reported it does not read the content type is dropped.** A channel that
   reported nothing has refused nothing, so it stays. `PrinterDevice.Accepts` is the
   judgement, and `NotSupportedException` is thrown only when every channel refused.
2. **A printer language takes a channel that sends the bytes unchanged.** ZPL, EPL, CPCL
   and ESC/POS are read by the printer firmware, so a channel that converts the job prints
   the command source instead of the label.
3. **Every other format takes a channel with a job queue**, so the job can be watched after
   it is sent.
4. **The identifier breaks the tie** between the channels that suit the payload. It never
   overrules the payload: a device with no reported identity names itself with the
   identifier of its preferred channel, so the two cannot be told apart.
5. **The most preferred channel is the fallback** when no channel suits the payload. The
   order is `ipps`, `ipp`, `spooler`, `raw`.

This is a real gain: a printer found through the spooler is printed to over its raw
channel, without the caller having to look for that channel.

| Channel | Sends the bytes unchanged |
| --- | --- |
| `raw` | Yes |
| `spooler` on Windows | Printer languages: yes (`RAW` data type). PNG/JPEG: no, they are rasterised with GDI. |
| `spooler` on Linux and macOS (CUPS) | No |
| `ipp`, `ipps` | No |

**A CUPS-only device still gets the label.** CUPS gives no promise: only a raw queue passes
the bytes to the backend untouched, a queue with a driver or a driverless (IPP Everywhere)
queue converts the job, and CUPS reports no dependable attribute that tells the two apart.
Rule 5 therefore sends over the CUPS queue anyway, as `application/vnd.cups-raw`, which is
the format a raw queue needs and which stops CUPS from re-typing the job. Sending is better
than refusing, because refusing helps nobody and the format is correct either way.

```csharp
var label = devices.First(d => d.Details.Name.Contains("Zebra", StringComparison.Ordinal));

PrinterPayload payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);
await manager.PrintAsync(label.Id, payload, null, cancellationToken).ConfigureAwait(false);
```

`GetStatusAsync` and `WatchJobAsync` carry no payload, so they always prefer a channel with
a job queue.

### Limiting the transports

`PrinterManagerOptions.Transports` lists the transports the manager may open, and it is read
from the options the manager was **built** with, never from the argument of `DiscoverAsync`:
a print carries no options, and the scope of one discovery is not a policy for every call.

```csharp
services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);
```

- **Membership is permission.** A channel on a transport the list leaves out is never
  opened, whatever the payload or the identifier would have ranked. A device that no allowed
  transport reaches throws `NotSupportedException`, and the message names the allowed set.
- **Position is preference, but only between the channels that suit the call equally.** The
  five rules above still run first. An order that outranked them would put IPP before the
  raw channel for a label, which is the defect this design exists to prevent.
- **Discovery is not affected.** An excluded channel is still found, still listed in
  `PrinterDevice.Channels`, and still contributes what it reported to `PrinterDevice.Details`.
  That is what lets an application learn everything about a printer and still print through
  one transport.
- **`WatchJobAsync` counts allowed channels only.** A queue on a transport the manager may
  not open is not a queue it can read.

The default is every transport, in the order `ipps`, `ipp`, `spooler`, `raw`.

Because the manager keeps these options, `DiscoverAsync(null, ...)` and the implicit
re-discovery inside `PrintAsync` both use them. A manager built with `IncludeMdns = false`
therefore never browses, even when a cache miss forces a fresh discovery.

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

**It tries every channel of the device, most preferred first, until one answers.** A printer
commonly advertises a channel it cannot actually serve — an `ipps` port whose certificate no
longer negotiates is the usual one — and the device is not unreachable while another of its
channels still answers. Only when none answers is the failure of the first one reported,
because that is the channel the caller asked for. A device that answers on its first channel
opens exactly one, as before.

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

`samples/Devices.Samples` is a small web application: a printer manager the whole library
can be driven from. Start it, and open the address it prints.

```bash
dotnetup dotnet run --project samples/Devices.Samples
```

It listens on `http://localhost:5080` and on the loopback address only, because it prints
to real hardware. Set `ASPNETCORE_URLS` to move it, and know what that means.

The page has a printer list on the left and four tabs on the right:

- **Print** — the channel, the file and the options, then one button. The file is one the
  sample ships in `PrintFiles/`, or one uploaded from the machine. A switch sends the bytes
  unchanged (raw) instead of through the queue. The options form offers only what the
  channel reported, so the page never invents a choice. Before the button, the page calls
  `PrinterDevice.Accepts` and warns when the channel says it does not read the format.
- **Job sets** — the scripted hardware test. A job set is a JSON file; the sample ships
  `PrintJobs/queue-sweep.json` (five documents through one queue, each changing one option
  from the job before it) and `PrintJobs/raw-sweep.json` (a JPEG, a ZPL label and an EPL
  label, unchanged). A set can also be uploaded, so the same test runs on Windows, Linux and
  macOS and the results compare job by job. The run ends with a summary of what became of
  each job.
- **Printer** — the status and the capabilities of the selected device, channel by channel.
- **Diagnostics** — which channels reach one queue (`QueueCorrelation`, with an optional
  tracer job), what one host answers over IPP and over SNMP, and the Windows spooler checks
  of [windows-manual-tests.md](windows-manual-tests.md).

A job reports its progress over minutes, so every flow that prints answers with a stream of
server-sent events. The browser shows each line as it arrives, and closing the page stops
the watch. The printer keeps the job.

### The job set format

```json
{
  "name": "Queue sweep",
  "description": "Each job changes one option from the job before it.",
  "mode": "queue",
  "jobs": [
    { "file": "document.pdf", "description": "a PDF with the printer defaults" },
    { "file": "image.png", "description": "a PNG in colour, at its own size",
      "options": { "colorMode": "Color", "scaling": "None" } }
  ]
}
```

`mode` is `queue` or `raw`. `options` holds the properties of `PrintOptions`, with an enum
written as its name and `pageRanges` written as `"1-3,5"`. `contentType` overrides what the
file extension says. The target printer is not in the set: the printer is what differs
between two machines, so the person selects it in the browser.

### The HTTP interface

| Method and path | What it does |
| --- | --- |
| `GET /api/printers` | The devices and their channels. `?refresh=true` browses again; `?probe=true` also probes the local subnet. |
| `GET /api/printers/status?id=` | The status of one printer. |
| `GET /api/printers/accepts?id=&contentType=` | The tri-state answer of `PrinterDevice.Accepts`. |
| `GET /api/files` | The files in `PrintFiles/`. |
| `POST /api/jobs` | Print a file from `PrintFiles/`. Answers with server-sent events. |
| `POST /api/jobs/upload` | The same, with the bytes in a multipart form. |
| `GET /api/job-sets` | The job sets in `PrintJobs/`. |
| `POST /api/job-sets/run` | Run a job set. The set is in the body, so an uploaded set and a supplied set take one path. |
| `POST /api/diagnostics/correlate` | Compare the job queues. `{"tracer": true}` permits a tracer job. |
| `GET /api/diagnostics/details?host=` | What one host answers over IPP and over SNMP. |
| `POST /api/diagnostics/windows-spooler` | The Windows spooler checks. |

Nothing prints until a `POST` arrives, and the browser asks the person first.

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

The **Print** tab calls it and warns before it wastes paper. A printer that reports nothing
did not refuse; it only did not answer.

To print a PDF on a printer that has no PDF interpreter, send it through the CUPS
spooler queue instead. The queue driver rasterises the document. On Windows, print
it through the spooler with the `AdaptArch.Devices.Windows` package enabled: each page
renders to PNG with the in-box engine and prints as one GDI document. Sending PDF
bytes as `RAW` reaches a firmware that reads only its own page language and prints
nothing while the spooler still reports success.

### Building the sample with trimming and native AOT

```bash
sh ./pipeline/publish-samples.sh -r linux-x64
```

The script publishes the sample three times — framework-dependent, trimmed self-contained,
and native AOT — into `./artifacts/samples/<rid>/`. Warnings stay errors, so an `IL2xxx` trim
warning or an `IL3xxx` AOT warning fails the publish. This is how a trim problem in the
library is found early. Use `-o` to select another output directory.

The web host is built for this. `WebApplication.CreateSlimBuilder` leaves out what a printer
manager never uses, every contract is serialized through a source-generated
`JsonSerializerContext`, and the project sets `EnableRequestDelegateGenerator`, which the SDK
otherwise turns on only for a trimmed or a native AOT publish. With it on, an endpoint shape
the generator cannot read fails an ordinary build instead of the publish at the end of the
day.

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
