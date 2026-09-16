# Troubleshooting

This page tells you how to find why a print job did not print.

## Read the job

A job that stops reports a state and a set of reasons. The reasons are protocol keywords:
they name *what* happened, not always *why*. The readable messages name why.

```csharp
PrintJobInfo job = await queue.GetJobAsync(printerId, jobId, cancellationToken);

foreach (string reason in job.StateReasons)
{
    Console.WriteLine(reason);                 // resources-are-not-ready
}

Console.WriteLine(job.StateMessage);           // "Job stopped."
Console.WriteLine(job.PrinterStateMessage);    // "Unable to connect: certificate expired."
```

Read `PrinterStateMessage` first: CUPS puts the text of its own log there, and that text is
usually the only place that names the true cause.

`StateReasons` holds one entry for each reason, so a caller can match one. `Detail` is the
same list joined into one line for a person to read. `PrinterStatus` carries the same
fields for the printer.

## See which transport answered

The library tries IPPS (TLS) first and plain IPP next, so a printer that fails the handshake
is reached over clear text without a word.

```csharp
PrinterStatus status = await printer.GetStatusAsync(cancellationToken);
Console.WriteLine(status.Connection?.Scheme);    // Ipp, and not Ipps
```

Set `IppTransportOptions.AllowPlainIpp` to `false` to refuse the downgrade.

## Turn on the log

The library writes in four categories that nest under `AdaptArch.Devices.Printing`: the
manager, the IPP wire, the discovery sources and the Windows spooler. One filter rule turns
on all of them.

```csharp
services.AddLogging(builder => builder.AddConsole()
    .AddFilter("AdaptArch.Devices", LogLevel.Debug));
services.AddPrinters();
```

The level says what the library did about a failure:

| Level | What it means |
| :--- | :--- |
| `Error` | The library swallowed a failure and gave you a result anyway. Your `catch` block never saw it. |
| `Warning` | It continued with less than you asked for: a downgrade, a dropped option, a retry, a list that was cut short. |
| `Information` | A milestone. Low volume, and safe to leave on. |
| `Debug` | One entry for each operation and each decision. |
| `Trace` | One entry for each item. |

A failure that reaches your code as an exception stays at `Debug`, so the same failure is
never reported twice. A job name and a user name are personal data, so they are at `Debug`
and below only.

`AddDevices()` and `AddPrinters()` take the `ILoggerFactory` of the container and give it to
every part of the library. Without dependency injection, set
`PrinterManagerOptions.LoggerFactory` and `IppTransportOptions.LoggerFactory`, or the
`LoggerFactory` property of the type you build.

## Read a failure

`PrinterConnectionException.Failures` holds the cause of **each** endpoint that was tried,
so a TLS failure over IPPS is not hidden by a later "connection refused" over plain IPP.

`PrinterOperationException` carries `PrinterId`, `Endpoint`, `Operation` and `IppStatusCode`
as data, so a caller does not match on the message text. Both types derive from
`InvalidOperationException`, so an existing `catch` block still catches them.

## Read the raw answer

When no mapped field names the cause, set `IppTransportOptions.CaptureRawResponses` and read
`RawAttributes` on the status, the job or the exception. Keep the switch off in normal
operation: it holds every attribute of every answer in memory.

The full guide, with the event table and the field-by-channel table, is in
[the repository documentation](https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md).
