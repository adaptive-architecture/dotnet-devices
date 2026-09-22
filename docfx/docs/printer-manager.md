# Printer manager

`IPrinterManager` is the layer most callers want. It runs every discovery source, reports one
`PrinterDevice` for each physical printer, and picks the channel each call needs.

```csharp
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();
var devices = await manager.DiscoverAsync(null, cancellationToken).ConfigureAwait(false);

var label = devices.First(d => d.Details.Name.Contains("Zebra", StringComparison.Ordinal));
PrinterPayload payload = PrinterPayload.FromString(
    "^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

await manager.PrintAsync(label.Id, payload, null, cancellationToken).ConfigureAwait(false);
```

**`DiscoverAsync` returns devices, not channels.** One physical printer is one `PrinterDevice`,
however many ways it can be reached. Its channels are on `Channels`, ordered the way the
manager prefers them, and grouped on `ChannelsByTransport`.

## The four phases

1. **Discover.** Every configured source runs at once and reports the channels it found.
2. **Enrich.** When the caller asked for it, each channel is opened once and asked what it
   supports and which device it belongs to. Opt-in, because every answer costs a request.
3. **Correlate.** When the caller asked for it, the IPP channels no source named a device for
   are asked what is in their queue, and the ones that answer with the same jobs are merged.
   Opt-in, and opt-in again before it writes anything.
4. **Group.** The channels are put into devices.

## Which sources run

`PrinterManagerOptions` controls this. Give it to each `DiscoverAsync` call, because the
manager does not keep it.

| Source | Property | Default |
| :--- | :--- | :--- |
| Operating system spooler and named CUPS servers | `IncludeSpooler` | On |
| mDNS browse | `IncludeMdns`, and `Mdns.ServiceTypes` for each service type | On |
| TCP probe | `Probe`, which lists the hosts to open a connection to | Off |

**The TCP probe never runs on its own.** It needs a host list, so it runs only when `Probe`
carries one. To change how long the browse waits, set `Mdns.BrowseTimeout`; the timeout is not
on `PrinterManagerOptions` itself.

**All three sources can run, or none of them.** When every source is off, `DiscoverAsync`
returns an empty list. It does not throw: no source ran, so no source failed.

**`DiscoverAsync` throws `PrinterDiscoveryException` only when every source failed.** `Failures`
holds the error of each source. A source that failed while another answered is not reported —
read the log for that, at event 2000.

## Reading capabilities and identity

Two more properties ask each channel for more data. Both are off by default and each costs one
request per channel.

| Property | What it adds |
| :--- | :--- |
| `ReadCapabilities` | The document formats, the media, the resolutions and the duplex support of each channel. Fills `DiscoveredPrinter.Configuration`, which stays `null` — meaning "not read" — when it is off. |
| `ReadIdentity` | The UUID, the serial number and the device URI. This is what merges the channels of one printer when the browse alone reported no identity. |

`MaxEnrichmentConcurrency` bounds how many channels are read at once. It defaults to eight,
because a probe of a whole subnet can return hundreds of channels.

**A channel that does not answer never fails the discovery.** It is reported exactly as the
browse found it, with no capabilities and no identity.

```csharp
var devices = await manager.DiscoverAsync(
    new PrinterManagerOptions { ReadIdentity = true, ReadCapabilities = true },
    cancellationToken).ConfigureAwait(false);
```

## Which print options a channel applies

`DiscoveredPrinter.SupportedOptions` names the `PrintOptions` properties that channel applies.
Every other option is dropped or ignored, whatever the caller sets. The value comes from the
transport, which the library knows without asking anything, and is narrowed by the capabilities
when those were read.

| Channel | Applies |
| :--- | :--- |
| `raw` | Nothing. The payload reaches the device unchanged. |
| `spooler` on Windows | `JobName`, `Copies`, `Duplex`, `ColorMode`, `Orientation`, `MediaSource`, `MediaSize`, `ResolutionDpi`, `Quality`, `Placement`, `Smoothing` and `MediaDimensions`. Printer languages report the rest in `PrintJobInfo.DroppedOptions`; image jobs apply every `Orientation` and every `Scaling` with GDI instead of the device mode. |
| `spooler` on CUPS, `ipp`, `ipps` | Everything the library models, narrowed by what the printer reported. |

A capability the printer did not report is not one it denied, so only an explicit `false`
narrows the set.

## Choosing a channel for a call

**The payload chooses the channel, not a flag.** `PrintAsync` reads `payload.ContentType` and
picks the channel that suits it. There is nothing to switch on, because a caller who sends ZPL
never wants the bytes rewritten.

The rules run in this order:

1. **A channel that reported it does not read the content type is dropped.** A channel that
   reported nothing has refused nothing, so it stays. `PrinterDevice.Accepts` is the judgement,
   and `NotSupportedException` is thrown only when every channel refused.
2. **A printer language takes a channel that sends the bytes unchanged.** ZPL, EPL, CPCL and
   ESC/POS are read by the printer firmware, so a channel that converts the job prints the
   command source instead of the label.
3. **Every other format takes a channel with a job queue**, so the job can be watched after it
   is sent.
4. **The identifier breaks the tie** between the channels that suit the payload. It never
   overrules the payload.
5. **The most preferred channel is the fallback** when no channel suits the payload. The order
   is `ipps`, `ipp`, `spooler`, `raw`.

| Channel | Sends the bytes unchanged |
| :--- | :--- |
| `raw` | Yes |
| `spooler` on Windows | Printer languages: yes (`RAW` data type). PNG and JPEG: no, they are rasterised with GDI. |
| `spooler` on Linux and macOS (CUPS) | No |
| `ipp`, `ipps` | No |

This is a real gain: a printer found through the spooler is printed to over its raw channel,
without the caller having to look for that channel.

**A CUPS-only device still gets the label.** CUPS gives no promise — only a raw queue passes the
bytes to the backend untouched, and CUPS reports no dependable attribute that tells a raw queue
from a driver one. Rule 5 therefore sends over the CUPS queue anyway, as
`application/vnd.cups-raw`, which is the format a raw queue needs and which stops CUPS from
re-typing the job.

`GetStatusAsync` and `WatchJobAsync` carry no payload, so they always prefer a channel with a
job queue.

### Selecting a channel for one call

Give the identifier of that channel instead of `device.Id`:

```csharp
var queue = device.ChannelsByTransport[PrinterScheme.Spooler][0];
await manager.PrintAsync(queue.Id, payload, null, cancellationToken).ConfigureAwait(false);
```

**The content type still decides first.** On Windows this prints through the spooler for each
format, because the spooler sends the bytes unchanged. On Linux and macOS a CUPS queue does
not, so a printer language goes to the raw channel instead. Set `Transports` when you must have
the queue, or open the channel yourself with `IPrinterFactory`, which obeys nothing else.

## Limiting the transports

`PrinterManagerOptions.Transports` lists the transports the manager may open. It is read from
the options the manager was **built** with, never from the argument of `DiscoverAsync`: a print
carries no options, and the scope of one discovery is not a policy for every call.

```csharp
services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);
```

- **Membership is permission.** A channel on a transport the list leaves out is never opened,
  whatever the payload or the identifier would have ranked. A device that no allowed transport
  reaches throws `NotSupportedException`, and the message names the allowed set.
- **Position is preference, but only between the channels that suit the call equally.** The five
  rules above still run first. An order that outranked them would put IPP before the raw channel
  for a label, which is the defect this design exists to prevent.
- **Discovery is not affected.** An excluded channel is still found, still listed in
  `PrinterDevice.Channels`, and still contributes what it reported to `PrinterDevice.Details`.
  That is what lets an application learn everything about a printer and still print through one
  transport.
- **`WatchJobAsync` counts allowed channels only.** A queue on a transport the manager may not
  open is not a queue it can read.

The default is every transport, in the order `ipps`, `ipp`, `spooler`, `raw`.

### Get all the data, but print through the spooler

Let every source run and set both `Read*` properties on the discovery, then limit the transports
on the manager:

```csharp
PrinterManagerOptions rich = new()
{
    Probe = new() { Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts() },
    ReadIdentity = true,
    ReadCapabilities = true,
};
var devices = await manager.DiscoverAsync(rich, cancellationToken).ConfigureAwait(false);
```

```csharp
services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);
```

Each call then uses the queue, for each format and on each operating system, while
`PrinterDevice.Channels` still lists the raw and the IPP channels and `PrinterDevice.Details`
still holds what they reported.

## How channels are grouped into devices

Two channels are put on one device when a source **vouched** that they reach the same device, or
when they share an address. Vouching is evidence, never resemblance:

| Source | Evidence |
| :--- | :--- |
| mDNS | the `UUID` TXT record |
| IPP | `printer-uuid`, and the `SN` field of `printer-device-id` |
| SNMP | `prtGeneralSerialNumber` |
| CUPS | `device-uri` — a host, a USB serial, or a UUID |
| Windows spooler | a port name that is an address, such as `IP_192.168.1.5` |
| IPP job queue | the same not-completed jobs, reported by two channels, when queue correlation is on |
| TCP probe | none: a socket that accepted a connection says nothing about identity |

The device key is the strongest one in the set: a UUID, then a serial number, then a host, then
a queue name. A device that reported an identity therefore keeps its key when its address
changes.

**The library refuses to merge on anything else.** In particular:

- Two different hosts with no vouching alias stay two devices, **even with the same name, model
  and location**. Two identical printers on a shelf are two printers.
- A queue and a host stay apart with no `device-uri` or port name to link them, even when the
  two strings are equal.
- A loopback or unspecified address in an alias is ignored, or every CUPS queue on one machine
  would fuse into one device.
- A placeholder identity is ignored: a blank value, the empty UUID, `n/a`, `none`, `0`,
  `unknown`, a bare `SN:`, anything under three characters, or a value made of one repeated
  character. A whole fleet shipped with the same placeholder serial number would otherwise
  collapse into a single device.
- The CUPS `printer-uuid` is **never** used. CUPS mints it itself, as a hash over the server,
  the port and the queue name, so two queues to one printer report two different values. Only
  `device-uri` links a queue to its device.

**Grouping by address assumes a plain local network.** Behind a print server or a network
address translation, two devices can share a host, and the library cannot tell. Set
`ReadIdentity` where that matters.

## Correlating channels by their job queue

**A job in the queue of one channel is in the queue of another only when the two read one
queue.** That closes the case no identity read can: one printer at an IPv4 address, at an IPv6
address and at an mDNS host name, or on two network interfaces, with no `printer-uuid` and no
usable serial number to tie them together.

It is off by default, and it costs a write to a real printer to reach its second stage. Read
this section before turning it on.

```csharp
services.AddPrinters(configureManager: options =>
{
    options.ReadIdentity = true;
    options.QueueCorrelation = new QueueCorrelationOptions();
});
```

**Two consents, not one.** The options object itself says "you may open and read". Its
`AllowTracerJob` says "you may write". One flag would let a caller who wanted a queue read
silently acquire a job submission.

**The policy is read from the manager and never from the argument of `DiscoverAsync`**, for the
same reason `Transports` is: the argument scopes one discovery, and consent to write to a
printer is not a scope.

**It proves one queue, not one engine.** A class or a pool spreads one queue over several
devices, so a match is not a statement about which machine feeds the paper.

### What a queue can and cannot prove

| Channel | Sees a job another channel put in a queue | Why |
| :--- | :--- | :--- |
| `ipp`, `ipps` on the same queue | Yes | `Get-Jobs` returns it |
| `raw` | No | The channel has no queue |
| SNMP | No | The Printer MIB has no job table, and a held job never reaches the device |
| `spooler` | No | The job waits in that spooler and the device is never told. A queue is already tied to its device by `device-uri` or a port name, which is stronger evidence |

So this is evidence between IPP channels and nowhere else. Only `ipp` and `ipps` channels on a
transport the manager may open are candidates, and only ones no identity has already grouped —
setting `ReadIdentity` as well therefore makes the correlation cheaper and not dearer.

### Stage one: compare the queues that are already there

Each candidate is asked for its not-completed jobs, with one `requesting-user-name` for the
whole run, so a printer that scopes an answer by owner scopes both sides the same way. Nothing
is written.

**A job counts as evidence only when it is distinctive.** It needs an identifier, a creation
time, and a name that is neither blank nor one every second job carries — `Document`,
`Untitled`, `Test Page`, `(stdin)`. `time-at-creation` is seconds since that printer powered up,
so a coincidence would need two devices to agree on the same identifier, the same human-chosen
name, the same owner and the same uptime to the second.

**Two queues match only when their distinctive jobs are exactly equal, as a set.** One job in
common is not enough: an overlap rule would merge a member of a class with the class itself. A
queue with no distinctive job is no answer at all, and never a match with another queue that
also had none — two idle printers are still two printers.

### Stage two: one tracer job

When a queue held nothing distinctive, the only way to make it say something is to put something
in it. `AllowTracerJob` permits that.

```csharp
options.QueueCorrelation = new QueueCorrelationOptions { AllowTracerJob = true };
```

**A tracer is a `Create-Job` that is never given a document.** It is not a held print job. A job
that carries no document prints nothing whatever the printer does with the hold, so the
guarantee is structural and not a gate the library checks. `job-hold-until = indefinite` is sent
as well, so the tracer does not sit at the head of the queue holding up the next job, and a
channel that does not report both `Create-Job` and an indefinite hold is never asked. There is
no fallback that sends a document.

The job name is `adaptarch-devices-correlation-` and a fresh identifier, so a job left behind
names itself to whoever finds it. **One tracer is created on every unproven channel before any
queue is read again**: each name is unique, so a single re-read reveals every pairing at once,
and the whole stage costs one write per channel rather than one per pair.

**Every tracer is cancelled in a `finally`, on a token of its own.** This is the one place in
the library that ignores the caller's cancellation: a discovery that was cancelled must still
take its job back out of the queue. A tracer whose `Create-Job` answer was lost is found again
by its name in the re-read and cancelled too.

**A tracer is never created while the manager is only refreshing to resolve an identifier.**
`PrintAsync` with an identifier the cache does not hold triggers a discovery, and printing one
label must not leave a job on every idle printer on the network.

### What it costs

With *n* candidates: *n* reads for stage one, and for stage two one capability read, one
`Create-Job`, one re-read and one `Cancel-Job` each. Never *n²* writes.
`MaxEnrichmentConcurrency` bounds how many channels are read at once, and `MaxChannels` (sixteen
by default) refuses the whole correlation rather than correlating an arbitrary subset, because a
partial answer would depend on which channel sorted first.

**A correlation that failed proves nothing and never fails a discovery that worked.** Events
3030 to 3037 record what it did; 3034 records each tracer written to a real printer.

## Resolving an identifier

- **An address form is opened directly.** It names its own endpoint, so no browse and no probe
  runs. A host that answers nothing is not an addressing problem the manager reports: the
  transport error is the answer, and it names the address.
- **An identity form runs one fresh discovery**, because a UUID names no address. The call
  throws `InvalidOperationException` only when the identifier is still unknown after that.
- **Every key of a device resolves to it**: its own key and every alias a source vouched for. An
  identifier kept from before an identity was read therefore keeps working.
- **A stale entry does not repair itself.** The manager does not re-discover after a failed
  print, because most print failures are not addressing problems: no paper, no permission, a
  rejected option. Call `DiscoverAsync` to refresh.
- **`DiscoverAsync` does not remove cache entries.** A printer removed from the network stays in
  the cache, and every print to it keeps failing.

Because the manager keeps its own options, `DiscoverAsync(null, …)` and the implicit
re-discovery inside `PrintAsync` both use them. A manager built with `IncludeMdns = false`
therefore never browses, even when a cache miss forces a fresh discovery.
