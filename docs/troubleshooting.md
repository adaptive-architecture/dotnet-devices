# Troubleshooting

This page tells you how to find why a print job did not print. Read the data the library
gives first, then turn on the log, then read the raw answer.

## Why does this job not print?

A job that stops reports a state and a set of reasons. The reasons are keywords of the
protocol, and they name *what* happened. They do not always name *why*. The readable
messages do.

```csharp
PrintJobInfo job = await queue.GetJobAsync(printerId, jobId, cancellationToken)
    .ConfigureAwait(false);

Console.WriteLine(job.State);                  // Failed
foreach (string reason in job.StateReasons)
{
    Console.WriteLine(reason);                 // resources-are-not-ready
}

Console.WriteLine(job.StateMessage);           // "Job stopped."
Console.WriteLine(job.PrinterStateMessage);    // "Unable to connect: certificate expired."
```

- **`StateReasons`** holds one entry for each reason. Match one entry to act on it; do not
  parse `Detail`, which is the same list joined into one line for a person to read.
  A job with no reason gives an empty list. The protocol keyword `none` means "no reason at
  all", so it is never an entry.
- **`StateMessage`** is the `job-state-message` attribute: what the print server says about
  the job.
- **`PrinterStateMessage`** is the `job-printer-state-message` attribute: what the printer
  said while it held the job. **CUPS puts the text of its own log here**, which is usually
  the only place that names the true cause. Read this first.
- **`DetailedStatusMessages`** holds the `job-detailed-status-messages` attribute. It is for
  a person to read. Do not parse it.

`PrinterStatus` carries the same four fields for the printer: `StateReasons`,
`StateMessage`, `DetailedStatusMessages` and `Detail`.

### An example: `resources-are-not-ready`

A job stops with the reason `resources-are-not-ready`, and the printer reports
`cups-pki-expired`. Neither keyword tells you what to do. `PrinterStateMessage` does: the
print server could not open the TLS connection, because the device certificate is expired.
Replace the certificate on the device, or point the queue at the plain port.

## Which transport answered?

The library tries IPPS (TLS) first, and plain IPP next. A printer that fails the handshake
is then reached over clear text, and nothing tells you unless you look.

```csharp
PrinterStatus status = await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
Console.WriteLine(status.Connection?.Scheme);    // Ipp, and not Ipps
Console.WriteLine(status.Connection?.Endpoint);  // ipp://printer.local:631/ipp/print
```

`IppPrinter.Connection` and `IppPrintJobQueue.Connection` report the same thing. Both are
`null` until the first call finds an endpoint, and a transport failure clears them, so the
next call probes again.

Set `IppTransportOptions.AllowPlainIpp` to `false` to refuse the downgrade.

**A local CUPS queue always reports `Ipp`.** The local CUPS server listens on the IPP socket
of the machine, and the library makes no TLS attempt there, because there is nothing to
protect on the loopback. Read `Connection` to find a downgrade on a **network** printer. A
`spooler://` identifier is not one.

## Turn on the log

The library writes in four categories. Each one nests inside the one above, so a filter on
the root turns on everything.

| Category | What it records |
| :--- | :--- |
| `AdaptArch.Devices.Printing` | The printer manager, the channel it chose, the options it dropped, the jobs and the raw transport. |
| `AdaptArch.Devices.Printing.Ipp` | The IPP wire: each endpoint probe, each operation and its status code, each job read. |
| `AdaptArch.Devices.Printing.Discovery` | The multicast DNS browse, SNMP, the network probe and the queue correlator. |
| `AdaptArch.Devices.Printing.Spooler` | The Windows spooler. The CUPS spooler speaks IPP, so it writes the IPP events. |

With dependency injection, nothing more is needed: `AddDevices()` and `AddPrinters()` take
the `ILoggerFactory` of the container and give it to every part of the library.

```csharp
services.AddLogging(builder => builder.AddConsole()
    .AddFilter("AdaptArch.Devices", LogLevel.Debug));
services.AddPrinters();
```

Without dependency injection, set the factory on the two options objects:

```csharp
PrinterManagerOptions manager = new() { LoggerFactory = loggerFactory };
IppTransportOptions transport = new() { LoggerFactory = loggerFactory };
```

A type you build by yourself takes the factory through its own `LoggerFactory` property:
`MdnsPrinterDiscovery`, `SnmpPrinterStatusClient`, `TcpNetworkPrinterDiscovery`,
`TcpPrinterTransport`, `PollingPrintJobMonitor`, `RawPrinter`, `PrinterFactory`,
`SpoolerPrinter`, `SpoolerPrinterDiscovery` and `SpoolerPrintJobQueue`. A spooler type falls
back to the factory of its `IppTransport`, so setting only the transport policy is enough.

## What each level means

The level says what the library did about the failure, so a reader knows what to look for.

| Level | What it means |
| :--- | :--- |
| `Error` | **The library hid a failure from you.** It swallowed an exception and gave you a result anyway. Read every `Error` line: each one is something your application cannot see by itself. |
| `Warning` | The library continued, but with less than you asked for: a downgrade, a dropped option, a retry, a skipped packet, or a list that was cut short. |
| `Information` | A milestone: a discovery finished, a job was submitted, a job ended. Low volume, and safe to leave on in production. |
| `Debug` | One entry for each operation and each decision. This is the level to ask a user for. |
| `Trace` | One entry for each item: each host probed, each datagram, each reading of a job. Use it on one printer, not on a subnet scan. |

**A failure that reaches your code as an exception is never above `Debug`.** The exception
already carries it, and the library does not report the same failure twice. So an `Error`
line always means something your `catch` block never saw.

**A job name and a user name are personal data**, so they appear at `Debug` and `Trace` only.
An `Information` log carries identifiers, states and hosts, and no names.

## The events

An event identifier is stable. It is never reused for another meaning and never renumbered,
so "send us every line with event 2003" works against any version of the library.

### `AdaptArch.Devices.Printing`

| Event | Level | What it records |
| :--- | :--- | :--- |
| 2000 | Error | A discovery source failed while others answered, so printers are missing and nothing else says so. |
| 2001 | Debug | A discovery source failed and every source failed. The exception you get carries each cause. |
| 2002 | Debug | Channels that repeated were removed. |
| 2003 | Information | A discovery finished: printers, channels and how long it took. |
| 2010 | Warning | A capability read failed, so the configuration of that printer is unread. |
| 2011 | Warning | An identity read failed, so that channel may not group with its device. |
| 2012 | Warning | A channel could not be opened for enrichment. |
| 2020 | Warning | A channel could not be opened for correlation, so it gives no evidence. |
| 2021 | Error | Queue correlation failed, so one printer may show as more than one. |
| 2030 | Debug | A status channel did not answer. |
| 2031 | Warning | The status came from a fallback channel after another did not answer. |
| 2032 | Warning | A printer language goes to a channel with no passthrough, so it may print as text. |
| 2033 | Debug | The channel a job was routed to, and why. |
| 2034 | Warning | A queue read goes to a channel with no job queue. |
| 2035 | Debug | A printer was opened straight from its address, with no discovery. |
| 2036 | Information | A discovery ran because an identifier could not be resolved. |
| 2040 | Warning | The options a job lost, so it printed with the settings of the queue. |
| 2050 | Information | A job was submitted: the job, the printer, the endpoint, the format and the size. |
| 2051 | Debug | The name of a job and the user who sent it. **Personal data.** |
| 2060 | Information | A job reached a terminal state. |
| 2061 | Information | A job left the queue after it was seen printing. This is the normal end on CUPS. |
| 2062 | Warning | A job left the queue **before** it printed. It is reported complete, so a cancel and a purge look the same as a success. |
| 2063 | Debug | Why a watch ended, and how many readings it took. |
| 2064 | Trace | One reading of a job. |
| 2070 | Error | The close of a raw channel failed. Some printers print the last page only after this. |
| 2071 | Information | A raw job was written. A raw channel has no queue, so nothing else reports it. |
| 2072 | Debug | SNMP did not answer for a raw printer, so IPP was tried. |
| 2073 | Error | Neither SNMP nor IPP answered, so the state is reported as Unknown. |

### `AdaptArch.Devices.Printing.Ipp`

| Event | Level | What it records |
| :--- | :--- | :--- |
| 1001 | Debug | An endpoint probe answered. |
| 1002 | Debug | An endpoint probe did not answer, with its cause. |
| 1003 | Debug | The endpoint that serves a printer. |
| 1011 | Debug | An IPP operation finished, with its IPP status code. |
| 1012 | Debug | An IPP operation failed, with its IPP status code and its cause. |
| 1021 | Debug | A job was read, with its state, its reasons and its message. |
| 1030 | Debug | The document format a job was sent with. |
| 1031 | Warning | The printer listed no format it knows, so the job went as `application/octet-stream`. **This is the cause of a label that prints as a page of source.** |

### `AdaptArch.Devices.Printing.Discovery`

| Event | Level | What it records |
| :--- | :--- | :--- |
| 3000 | Warning | The browse found no usable network interface, so it reports no printers. |
| 3001 | Warning | A responder sent a packet the browse cannot read, with its address. |
| 3002 | Warning | A browse channel failed; the records that arrived are kept. |
| 3003 | Warning | The browse stopped at its record limit, so printers may be missing. |
| 3004 | Warning | The socket of one interface was refused, so that interface is not browsed. |
| 3005 | Warning | How many unreadable packets were skipped, and by how many responders. |
| 3006 | Debug | The browse finished: records, channels and printers. |
| 3010 | Debug | One SNMP attempt failed. |
| 3011 | Warning | The agent answered tooBig, so the walk asks for fewer rows. |
| 3012 | Warning | The supply walk stopped at its round limit, so supplies are missing. |
| 3013 | Warning | An agent sent a datagram the client cannot read, with its address. |
| 3014 | Trace | A late answer to an earlier attempt was discarded. |
| 3015 | Debug | An SNMP request did not answer in time. |
| 3017 | Warning | How many unreadable SNMP datagrams were skipped. |
| 3020 | Trace | One host refused the probe. |
| 3021 | Trace | One host did not answer the probe in time. |
| 3022 | Debug | The probe finished: hosts tried and hosts that answered. |
| 3030 | Warning | Correlation was skipped: too many candidate channels. |
| 3031 | Debug | Correlation was skipped: fewer than two candidates. |
| 3032 | Debug | Two channels were joined because their queues hold the same jobs. |
| 3033 | Debug | Two channels were joined because both hold one tracer job. |
| 3034 | Information | A tracer job was **written to a real printer** to correlate its queue. |
| 3035 | Error | A tracer job was not cancelled and may stay in that queue. |
| 3036 | Warning | One correlation step failed, so that channel gives no evidence. |
| 3037 | Debug | Correlation finished: channels read and queues proved. |

### `AdaptArch.Devices.Printing.Spooler`

| Event | Level | What it records |
| :--- | :--- | :--- |
| 4000 | Debug | The Windows spooler took a job. |
| 4001 | Debug | How many queues the Windows spooler reported. |
| 4011 | Error | The identity of a queue was not read, so its port aliases are lost and it may show as a printer of its own. |
| 4020 | Warning | The options the Windows driver did not apply. |

An application that sets no factory writes nothing and pays almost nothing: each event stops
at its "is this level on?" test.

## Send us your log

Run the application again with this filter, do the thing that fails, and send the output:

```csharp
services.AddLogging(builder => builder.AddConsole()
    .AddFilter("AdaptArch.Devices", LogLevel.Debug));
```

Every line names the printer, the job or the host it is about, so a log pasted into an issue
needs no other configuration and no scope support from your logger.

Two notes:

- **`Trace` on a subnet scan is one line for each host.** Turn it on for one category at a
  time, for example `AddFilter("AdaptArch.Devices.Printing.Discovery", LogLevel.Trace)`.
- **`Debug` carries job names and user names.** Read the output before you send it if those
  are personal data where you work. `Information` carries none.

## Read a failure

### No endpoint answered

`PrinterConnectionException` holds the cause of **each** endpoint that was tried, not only
the last one. This matters: a TLS handshake that failed over IPPS is the cause you need, and
the "connection refused" of a later plain IPP port would hide it.

```csharp
catch (PrinterConnectionException exception)
{
    foreach ((Uri endpoint, Exception cause) in exception.Failures)
    {
        Console.WriteLine($"{endpoint}: {cause.Message}");
    }
}
```

`InnerException` is the **first** failure, for the same reason. Read it for that first
cause, and not the first entry of `Failures`: a dictionary promises no order.

### The printer reported an error

`PrinterOperationException` carries the cause as data, so you do not match on the message.

```csharp
catch (PrinterOperationException exception)
{
    Console.WriteLine(exception.PrinterId);      // ipp://printer.local
    Console.WriteLine(exception.Endpoint);       // ipps://printer.local:631/ipp/print
    Console.WriteLine(exception.Operation);      // Print-Job
    Console.WriteLine(exception.IppStatusCode);  // 1034
}
```

`IppStatusCode` is the status code of RFC 8011 section 13.1. Two values the library itself
acts on:

| Code | Name |
| :--- | :--- |
| `0x0406` | `client-error-not-found` |
| `0x040A` | `client-error-document-format-not-supported` |

Both exception types derive from `InvalidOperationException`, so an existing `catch` block
still catches them.

## Read the raw answer

When no mapped field names the cause, read what the printer sent. Turn the switch on:

```csharp
IppTransportOptions options = new() { CaptureRawResponses = true };
```

The attributes then reach `PrinterStatus.RawAttributes`, `PrintJobInfo.RawAttributes` (for a
read of one job) and `PrinterOperationException.RawAttributes`.

```csharp
foreach (IppAttributeSnapshot attribute in status.RawAttributes)
{
    Console.WriteLine($"{attribute.Group}/{attribute.Name} = {attribute.Value}");
}
```

`Group` is `operation`, `printer`, `job` or `unsupported`. The `unsupported` group names the
attributes the printer refused, which is what a rejected job option looks like.

Keep the switch off in normal operation: it holds every attribute of every answer in memory.

A read of the whole queue leaves `RawAttributes` empty, so one answer is not copied into
every job. Read one job to see its attributes.

The switch is part of `IppTransportOptions`, so it reaches every IPP path, including a
local CUPS queue: `AddPrinters(options => options.CaptureRawResponses = true)` covers a
`spooler://` printer on Linux and macOS as well. Without dependency injection, set the
`IppTransport` property of the spooler type you build.

## What each channel reports

| Field | IPP and CUPS | SNMP | Windows spooler |
| :--- | :--- | :--- | :--- |
| `Detail` | Yes | Yes | Yes |
| `StateReasons` | Yes | Yes | Yes |
| `StateMessage` | Yes | No | No |
| `DetailedStatusMessages` | Yes | No | No |
| `Connection` | Yes | No | No |
| `RawAttributes` | With the switch on | No | No |
| Log events | Yes | Yes | Yes |

The CUPS spooler speaks IPP, so a `spooler://` printer on Linux and macOS reports everything
the IPP column reports.

## Related pages

- [Printers](printers.md) — the printer abstractions, and the reasons behind them
- [Packages](packages.md) — why the logging dependency was approved
