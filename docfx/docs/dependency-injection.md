# Dependency injection

`AdaptArch.Devices.DependencyInjection` supplies `AddDevices()` and `AddPrinters()` for
`Microsoft.Extensions.DependencyInjection`. It is a separate package so the core library keeps
few dependencies.

```bash
dotnet add package AdaptArch.Devices.DependencyInjection
```

```csharp
using AdaptArch.Devices.DependencyInjection;

services.AddPrinters();
```

`AddDevices()` is the forward-looking name and today calls `AddPrinters()`. Either registers the
transports, the network discovery, the status clients, `IPrinterFactory`, `IPrintJobQueue`,
`IPrintJobMonitor` and `IPrinterManager`.

## What is registered, and why as singletons

Every printer type is registered as a singleton. Most hold no state between calls. Three hold
state that is safe to share:

- `CompositePrintJobQueue` keeps one queue per network host in a thread-safe dictionary.
- `SnmpPrinterStatusClient` keeps a request counter it updates atomically.
- `PrinterManager` keeps the device cache and a semaphore that stops several callers from all
  running a discovery at once. **That cache is why `PrinterManager` must stay a singleton.**

Every registration uses `TryAdd`, so a registration the application made first is left in place,
and a second call to `AddPrinters()` changes nothing.

## The two callbacks

```csharp
services.AddPrinters(
    // The IPP transport policy: certificate trust, the plain IPP fallback, the connect timeout.
    configure: transport =>
    {
        transport.AllowPlainIpp = false;
        transport.ConnectTimeout = TimeSpan.FromSeconds(3);
    },
    // The printer manager policy: the transports it may open, and the default discovery scope.
    configureManager: options =>
    {
        options.Transports = [PrinterScheme.Spooler];
        options.CupsServers.Add(new CupsServer("printsrv"));
    });
```

One `HttpClient` is built from the transport options, is shared by the factory, the status client
and the job queue, and is disposed with the container.

**A policy that belongs on the manager cannot be set per call.** `Transports`, `QueueCorrelation`
and the converter registrations are read from the options the manager was *built* with, never
from the argument of `DiscoverAsync`. A print carries no options of its own, and consent to write
to a printer is not the scope of one discovery. See
[Printer manager](printer-manager.md#limiting-the-transports).

## Logging

`AddPrinters()` takes the container's `ILoggerFactory` and hands it to every part of the library,
so one filter rule is all that is needed:

```csharp
services.AddLogging(builder => builder.AddConsole()
    .AddFilter("AdaptArch.Devices", LogLevel.Debug));
services.AddPrinters();
```

The resolution uses `GetService`, not `GetRequiredService`, so an application without logging
still resolves everything. An application that registers no factory writes nothing and pays
almost nothing. [Troubleshooting](troubleshooting.md#turn-on-the-log) describes the categories and
what each level means.

## Injecting them together

```csharp
public sealed class LabelService(
    IPrinterManager manager,
    IPrinterFactory factory,
    IPrintJobMonitor monitor)
{
    public async Task PrintAsync(PrinterId id, PrinterPayload payload, CancellationToken ct)
    {
        PrintJobInfo job = await manager.PrintAsync(id, payload, null, ct).ConfigureAwait(false);

        await foreach (var reading in monitor
            .WatchJobAsync(job.PrinterId, job.JobId, new PrintJobMonitorOptions(), ct)
            .ConfigureAwait(false))
        {
            // …
        }
    }
}
```

## Without a container

Nothing in the core package needs one. Build `PrinterFactory` and `PrinterManager` yourself and
set `PrinterManagerOptions.LoggerFactory` and `IppTransportOptions.LoggerFactory` by hand; the
types you construct directly each carry their own `LoggerFactory` property. A directly
constructed `IppPrinter` owns its `HttpClient` — see
[Printers](printers.md#disposal) for what owns what.
