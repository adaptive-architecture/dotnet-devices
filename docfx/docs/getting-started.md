# Overview

.NET Devices is a cross-platform library for interacting with hardware peripherals. Today it
prints: to a network printer over IPP or raw TCP, to a print queue of the operating system,
and to a CUPS server over the network. It runs on Windows, Linux and macOS.

## Design

The device abstractions live in one shared library. The platform behaviour is picked at run
time with an OS check, or at compile time where that is not possible. Every external device
interaction sits behind an interface, so a unit test can substitute it.

Two ideas carry most of the API:

- **Where a printer is** — an endpoint — is separate from **how the bytes get there** — a
  transport. A printer is usually reachable more than one way, and each way is a *channel*.
- **Print data is bytes plus a content type.** The content type is what decides which
  channel a job takes, so there is no flag to set and nothing to switch on.

## The packages

Install the core package, then add the one that matches what you print and where.

### `AdaptArch.Devices`

The abstractions and every implementation that needs no extra engine: IPP and IPPS, raw TCP,
the operating system spooler, CUPS over the network, discovery over mDNS and TCP, status over
IPP and SNMP, job queues and job monitoring.

```bash
dotnet add package AdaptArch.Devices
```

### `AdaptArch.Devices.DependencyInjection`

`Microsoft.Extensions.DependencyInjection` registrations, so the core package keeps few
dependencies. `AddPrinters()` registers everything as a singleton and hands the container's
`ILoggerFactory` to every part of the library.

```bash
dotnet add package AdaptArch.Devices.DependencyInjection
```

### `AdaptArch.Devices.Pdfium`

Renders a PDF with PDFium so a printer that cannot read one still prints it. Windows, Linux
and macOS, x64 and ARM alike. It carries the native library, which costs about 170 MB
restored and about 7.5 MB deployed for one runtime identifier.

```bash
dotnet add package AdaptArch.Devices.Pdfium
```

### `AdaptArch.Devices.Windows`

The same job through the in-box Windows engine, which downloads nothing. Windows 10 and
later, and Windows Server with the Desktop Experience.

```bash
dotnet add package AdaptArch.Devices.Windows
```

Either rasterizer serves any converting channel on Windows; only the PDFium one serves them
elsewhere. Neither is needed to print a PDF through a CUPS queue or to an IPP printer that
reads PDF. [Document formats](document-formats.md) has the whole table.

## The first print

```csharp
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Printing;

services.AddLogging(builder => builder.AddConsole());
services.AddPrinters();
```

```csharp
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();

// Every source runs: the operating system spooler and an mDNS browse of the local link.
IReadOnlyList<PrinterDevice> devices = await manager.DiscoverAsync(null, cancellationToken)
    .ConfigureAwait(false);

foreach (var device in devices)
{
    Console.WriteLine($"{device.Details.Name} — {device.Key}");
}

PrinterPayload payload = PrinterPayload.FromString(
    "^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

PrintJobInfo job = await manager.PrintAsync(devices[0].Id, payload, null, cancellationToken)
    .ConfigureAwait(false);

Console.WriteLine($"{job.JobId} went to {job.PrinterId}");
```

The payload was ZPL, so the manager sent it to a channel that passes the bytes through
unchanged — the raw channel, even on a printer that was found through the spooler. Nothing
in the call said so.

`job.PrinterId` is the channel the job really went to, which is not always the identifier you
printed with. Watch that one, never `devices[0].Id`:

```csharp
await foreach (var reading in manager.WatchJobAsync(
    job.PrinterId, job.JobId, new PrintJobMonitorOptions(), cancellationToken).ConfigureAwait(false))
{
    Console.WriteLine($"{reading.State}: {reading.ImpressionsCompleted} pages");
}
```

A raw channel has no queue, so a label sent that way cannot be watched at all and the call
throws `NotSupportedException`. [Status and monitoring](status-and-monitoring.md) says which
channels have a queue and how to end a watch.

## Where to go next

- [Printers](printers.md) — identifiers, endpoints, channels and devices, and how a job is
  sent.
- [Document formats](document-formats.md) — what is sent for each content type, and what
  happens to a document the printer cannot read.
- [Printer manager](printer-manager.md) — the layer most callers want, and the options that
  govern it.
- [Troubleshooting](troubleshooting.md) — find why a job did not print.
