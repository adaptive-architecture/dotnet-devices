# Status and monitoring

Reading what a printer says about itself, and following a job until it ends.

## Printer status over IPP

`IppPrinterStatusClient` reads identity and status with IPP Get-Printer-Attributes, and returns
make and model, state, state reasons and the supply markers. All operations are read-only.

```csharp
IppPrinterStatusClient client = new();
IppPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken)
    .ConfigureAwait(false);
```

`PrinterStatus` reports the state reasons three ways, and each one has a purpose:

- **`StateReasons`** holds one entry for each reason. **Match this one**, for example
  `cups-pki-expired`. A printer with no reason gives an empty list: the protocol keyword `none`
  means "no reason at all", so it is never an entry.
- **`Detail`** holds the same list joined with `"; "`, for a person to read.
- **`StateMessage`** and **`DetailedStatusMessages`** hold `printer-state-message` and
  `printer-detailed-status-messages`: free text the printer wrote. Do not parse either one.

`PrintJobInfo` carries the same four fields for a job, plus `PrinterStateMessage`, which is the
`job-printer-state-message` attribute. **CUPS puts the text of its own log there**, so it is
usually the only field that names why a job stopped. [Troubleshooting](troubleshooting.md) works
through that case.

The client tries IPPS (TLS) first and falls back to plain IPP, across `/ipp/print` and
`/ipp/port1`. The optional `resourcePath` parameter is tried before those well-known paths: pass
the `rp` attribute of a DNS-SD TXT record to reach a printer that serves IPP elsewhere.

The default constructor owns an `HttpClient` and disposes it; a client built from a
caller-supplied `HttpClient` does not. Neither this client nor `SnmpPrinterStatusClient`
implements an interface, so a test cannot mock either one — wrap the class behind your own seam
when a test must replace it.

## IPP transport policy

Every IPP connection this library opens follows one `IppTransportOptions`, whichever type opened
it. The connection opens over IPPS (TLS) first, and the plain IPP endpoints are tried next when
no IPPS endpoint answers.

| Option | Default | What it does |
| :--- | :--- | :--- |
| `AllowPlainIpp` | `true` | Many label printers speak plain IPP only, so the fallback is on. Set it to `false` to talk to IPPS printers only. |
| `ServerCertificateValidation` | `null` | The default accepts every certificate, because network printers use self-signed certificates in nearly every case. |
| `ConnectTimeout` | 5 seconds | Without it, the operating system default applies, which can be minutes. |

The default certificate policy **protects the print data against a passive observer only. It
does not prove that the host is the printer you expect.** Set `ServerCertificateValidation` when
the application must know. A printer has no certificate chain to a public root, so pin its
thumbprint:

```csharp
IppTransportOptions options = new()
{
    ServerCertificateValidation = (_, certificate, _, _) =>
        certificate is not null &&
        String.Equals(certificate.GetCertHashString(), knownThumbprint, StringComparison.OrdinalIgnoreCase),
};
using PrinterFactory factory = new(options);
```

**A validating client never falls back to plain IPP.** When `ServerCertificateValidation` is set,
or when you pass your own `HttpClient`, a failed TLS handshake throws `AuthenticationException`,
because clear text would defeat the trust you asked for. With the default policy, a failed
handshake means "this port speaks plain IPP", and the fallback runs.

To share one client between several types, build it once with `IppHttpClientFactory.Create(options)`
and pass the client together with the same options to each constructor. That factory sets the
connect timeout, turns off redirects (every IPP operation is a POST that carries the document),
and installs the certificate policy. The caller owns that client.

## Printer status over SNMP

`SnmpPrinterStatusClient` reads the Printer MIB (RFC 3805) and the Host Resources MIB from a host
you already know. Many printers supply these and do not answer IPP, and SNMP also reports the
serial number and the page count, which IPP does not carry. All operations are read-only.

```csharp
SnmpPrinterStatusClient client = new();
SnmpPrinterDetails details = await client.GetDetailsAsync("192.168.1.50", cancellationToken)
    .ConfigureAwait(false);
Console.WriteLine($"{details.Info.Name}: {details.Status.SerialNumber}, {details.Status.LifetimePageCount} pages");
```

- `SnmpPrinterStatusOptions` sets the community, the request timeout and the retry count (two,
  which gives three attempts, because UDP can lose a datagram). The client throws
  `InvalidOperationException` when no attempt is answered. A datagram from another address, or a
  malformed datagram, is not an answer and is discarded.
- A host name that resolves to both address families is queried over IPv4, because most printers
  answer SNMP on IPv4 only.
- When the agent answers the supply walk with `tooBig`, the client asks again once for half as
  many rows. Every other SNMP error status is an `InvalidOperationException`.
- `hrPrinterStatus` gives the state. The bits of `hrPrinterDetectedErrorState` can raise it to
  `Error` or `Offline`, and every set bit is named in `PrinterStatus.StateReasons`. A bit that is
  only a warning, such as `lowToner`, does not change the state, because a printer low on toner
  still prints.
- The Printer MIB uses a negative supply level for a value that is not a quantity: `-1` is
  "other", `-2` is "unknown amount remains" and `-3` is "some amount remains". For a negative
  level, `PrinterMarker.LevelPercent` is `null` and `LevelRaw` keeps the reported number.
- **This client does not search a network.** Find printers with a [discovery](discovery.md) first.
- Only SNMP version 2c is supported.

## Reading a status through the manager

`GetStatusAsync` resolves the identifier the same way `PrintAsync` does, and opens a channel a
status can be read from.

**It tries every channel of the device, most preferred first, until one answers.** A printer
commonly advertises a channel it cannot actually serve — an `ipps` port whose certificate no
longer negotiates is the usual one — and the device is not unreachable while another of its
channels still answers. Only when none answers is the failure of the first one reported, because
that is the channel the caller asked for.

## Job queues

`IPrintJobQueue` inspects and manages the jobs of a printer. `CompositePrintJobQueue` routes a
`spooler` identifier to the operating system spooler and a network identifier to IPP.

**The IPP queue states which attributes it wants.** A printer left to its own default answers
Get-Jobs with the job identifier alone, and none of the messages that say why a job stopped. Both
`GetJobsAsync` and `GetJobAsync` therefore send an explicit `requested-attributes` list that
holds every attribute the mapper reads.

## Watching a job

`IPrintJobMonitor.WatchJobAsync` yields a reading each time the state or the progress of one job
changes, until the job reaches a terminal state or leaves the queue. `PollingPrintJobMonitor`
reads the queue again and again, so it works with every printer and needs no notification
channel.

```csharp
IPrintJobMonitor monitor = new PollingPrintJobMonitor(queue);
PrintJobMonitorOptions watch = new() { IdleTimeout = TimeSpan.FromMinutes(2) };

await foreach (var reading in monitor
    .WatchJobAsync(job.PrinterId, job.JobId, watch, cancellationToken)
    .ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

**Watch `job.PrinterId`, and not the identifier you printed with.** A job queue resolves nothing,
so it needs an identifier that names a host. An identifier that names a device identity — which
is what mDNS discovery gives every printer that advertises a UUID, and a CUPS queue does — prints
through `IPrinterManager`, because the manager resolves it, and then fails the watch with
`NotSupportedException`. `PrintJobInfo.PrinterId` is the identifier of the channel the job really
went to, so it always names a host.

**A device with only a raw channel cannot be watched at all.** `WatchJobAsync` checks for a job
queue first and throws `NotSupportedException` when there is none. This refusal is correct, not a
limitation: a raw channel gives back a generated job identifier and has no queue to read.
Watching such a job through IPP would ask the wrong protocol about a job it never saw, and a past
defect showed the cost — the empty answer read as "the job is done", so the caller was told the
label was finished before the printer did any work.

### When a watch ends

**Only the `CancellationToken` throws. Every limit ends the watch quietly.** The watch stops and
the loop simply finishes; a watch whose last reading was not terminal ended early, and that last
reading is how a caller says so.

| Limit | Ends the watch when |
| :--- | :--- |
| `IdleTimeout` | Neither the state nor the page count has moved for that long. |
| `Timeout` | That long has passed in total, whatever the job is doing. |

**Prefer `IdleTimeout`.** Elapsed time does not separate a slow job from a stuck one; change
does. A printer that wakes from sleep can take minutes over the first page and then print
steadily: every page resets `IdleTimeout`, so the watch survives, while `Timeout` would end a job
that is working perfectly. Set `Timeout` only as an outer bound. Both may be set, and whichever
comes first ends the watch.

**Never pass a deadline as the cancellation token.** The monitor cannot tell a caller's deadline
from a real interruption, so it does what the token says and throws. A past defect showed the
cost: one `CancellationTokenSource(2 minutes)` covered a file read, the submission and the watch,
and a printer that woke from sleep and printed slowly took the whole run down with an unhandled
`TaskCanceledException`. The token is for a caller that wants to stop; a limit belongs in the
options.

**A sleeping printer needs no waking.** Submitting a job wakes it — that is what IPP and the
spooler already do — but the first page may take minutes while it warms up. During that time the
job reports `Printing` with no page finished, which is exactly the case `IdleTimeout` is sized
for.
