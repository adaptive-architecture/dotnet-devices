using System.Text;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples;

// Everything here goes through IPrinterManager: one entry point for discovery, printing,
// and job progress. This is the recommended path for a caller that just wants to print.
internal static class PrinterManagerScenario
{
    internal static async Task DiscoverAsync(ServiceProvider provider)
    {
        var printers = await BrowseAsync(provider).ConfigureAwait(false);
        if (printers.Count == 0)
        {
            // No printer advertises itself, so fall back to a probe of every local address.
            printers = await ProbeAsync(provider).ConfigureAwait(false);
        }

        if (printers.Count == 0)
        {
            Console.WriteLine("No printers found on the local network.");
            return;
        }

        var manager = provider.GetRequiredService<IPrinterManager>();
        foreach (var printer in printers)
        {
            Console.WriteLine($"- {printer.Info.Name} — {printer.Id} ({printer.Endpoint}) [{printer.Source}]");
            await PrintManagerStatusAsync(manager, printer.Id).ConfigureAwait(false);
        }
    }

    internal static async Task<IReadOnlyList<DiscoveredPrinter>> BrowseAsync(ServiceProvider provider)
    {
        // The manager also checks the operating system print spooler alongside the mDNS
        // browse, so a queue can appear below even when nothing answers on the network.
        Console.WriteLine("Asking the local network for printers over mDNS, and checking the print spooler...");
        var manager = provider.GetRequiredService<IPrinterManager>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(30));
        try
        {
            return await manager.DiscoverAsync(null, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"The browse failed: {exception.Message}");
            return [];
        }
    }

    internal static async Task<IReadOnlyList<DiscoveredPrinter>> ProbeAsync(ServiceProvider provider)
    {
        var hosts = SampleHelpers.GetLocalSubnetHosts();
        Console.WriteLine($"No printer answered. Probing {hosts.Count} local hosts on TCP port 9100...");
        var manager = provider.GetRequiredService<IPrinterManager>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
        PrinterManagerOptions options = new()
        {
            // The browse and the spooler enumeration already ran, and found nothing, in
            // the caller that falls back here. Running them again would repeat work that
            // just came back empty.
            IncludeMdns = false,
            IncludeSpooler = false,
            Probe = new NetworkPrinterDiscoveryOptions
            {
                Hosts = hosts,
                ConnectTimeout = TimeSpan.FromMilliseconds(500),
                MaxDegreeOfParallelism = 64,
            },
        };

        try
        {
            return await manager.DiscoverAsync(options, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Probe timed out before completing.");
            return [];
        }
    }

    internal static async Task SendAsync(ServiceProvider provider, string directory, string identifierText, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var path = Path.Combine(directory, safeFileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"File '{safeFileName}' not found in PrintFiles.");
            return;
        }

        // The manager opens the printer, prints, and disposes it, so this call owns
        // nothing and needs no disposal here.
        var manager = provider.GetRequiredService<IPrinterManager>();
        var printerId = SampleHelpers.ParsePrinterId(identifierText);
        // A network identifier that the cache does not hold is opened by host, so no browse
        // runs. A spooler identifier runs one discovery. Either way the manager resolves it.
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
        var bytes = await File.ReadAllBytesAsync(path, timeoutSource.Token).ConfigureAwait(false);

        PrintJobInfo submitted;
        try
        {
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
                new PrintOptions { JobName = safeFileName },
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{printerId} was not found; nothing was sent.");
            return;
        }

        Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
    }

    internal static async Task WatchAsync(ServiceProvider provider, string directory, string identifierText, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var path = Path.Combine(directory, safeFileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"File '{safeFileName}' not found in PrintFiles.");
            return;
        }

        // The manager opens the printer, prints, and disposes it, so this call owns
        // nothing and needs no disposal here.
        var manager = provider.GetRequiredService<IPrinterManager>();
        var monitor = provider.GetRequiredService<IPrintJobMonitor>();
        var printerId = SampleHelpers.ParsePrinterId(identifierText);
        // A network identifier that the cache does not hold is opened by host, so no browse
        // runs. A spooler identifier runs one discovery. Either way the manager resolves it.
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
        var bytes = await File.ReadAllBytesAsync(path, timeoutSource.Token).ConfigureAwait(false);

        PrintJobInfo submitted;
        try
        {
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
                new PrintOptions { JobName = safeFileName },
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{printerId} was not found; nothing was sent.");
            return;
        }

        Console.WriteLine($"Job {submitted.JobId} submitted.");

        // A network identifier without an IPP answer opens as a RawPrinter: the raw
        // channel has no job queue, so PrintAsync already reports the job Completed and
        // there is nothing left to watch.
        if (submitted.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
        {
            Console.WriteLine($"  {submitted.State}");
            return;
        }

        await foreach (var reading in monitor.WatchJobAsync(
            printerId, submitted.JobId, new PrintJobMonitorOptions(), timeoutSource.Token).ConfigureAwait(false))
        {
            Console.WriteLine($"  {SampleHelpers.DescribeJobReading(reading)}");
        }
    }

    // RequirePassthrough tells the manager the bytes must reach the device unchanged. A
    // printer with only a filtering channel cannot promise that, so the manager throws.
    internal static async Task SendZplAsync(ServiceProvider provider, string identifierText)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        var printerId = SampleHelpers.ParsePrinterId(identifierText);
        var payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));
        try
        {
            var submitted = await manager.PrintAsync(
                printerId,
                payload,
                new PrintOptions { RequirePassthrough = true },
                timeoutSource.Token).ConfigureAwait(false);
            Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
        }
        catch (NotSupportedException exception)
        {
            // The refusal is the guarantee working, not a failure. The console text says so.
            Console.WriteLine("The printer refused the job. This is the correct result.");
            Console.WriteLine("The printer has no channel that sends the bytes unchanged.");
            Console.WriteLine("A filtering channel could rasterise the ZPL and print the wrong output.");
            Console.WriteLine("Send without RequirePassthrough only when the printer reads the format you send.");
            Console.WriteLine($"Reason: {exception.Message}");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{printerId} was not found; nothing was sent.");
        }
    }

    // The status comes from IPrinterManager itself, the same entry point used to print, not
    // from a network-only client. A printer that fails to report status must not break the
    // rest of the listing, so the failure is caught and the entry stands without one.
    private static async Task PrintManagerStatusAsync(IPrinterManager manager, PrinterId id)
    {
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
        try
        {
            var status = await manager.GetStatusAsync(id, timeoutSource.Token).ConfigureAwait(false);
            StringBuilder line = new($"Manager: {status.State}");
            if (status.SerialNumber is not null)
            {
                line.Append($"; serial {status.SerialNumber}");
            }

            if (status.LifetimePageCount is not null)
            {
                line.Append($"; {status.LifetimePageCount} pages");
            }

            if (status.Detail is not null)
            {
                line.Append($"; {status.Detail}");
            }

            SampleHelpers.AppendMarkers(line, status.Markers);
            Console.WriteLine($"    {line}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"    Manager status unavailable: {exception.Message}");
        }
    }
}
