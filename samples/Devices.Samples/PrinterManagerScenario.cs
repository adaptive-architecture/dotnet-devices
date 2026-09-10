using System.Text;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples;

// The recommended path: IPrinterManager covers discovery, printing and job progress.
internal static class PrinterManagerScenario
{
    internal static async Task DiscoverAsync(ServiceProvider provider)
    {
        var printers = await BrowseAsync(provider).ConfigureAwait(false);
        if (printers.Count == 0)
        {
            // Nothing advertised itself, so probe every local address.
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
            // The caller that falls back here already browsed and found nothing.
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

        // The manager opens, prints and disposes, so nothing here needs disposal.
        var manager = provider.GetRequiredService<IPrinterManager>();
        var printerId = SampleHelpers.ParsePrinterId(identifierText);
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

        // The manager opens, prints and disposes, so nothing here needs disposal.
        var manager = provider.GetRequiredService<IPrinterManager>();
        var monitor = provider.GetRequiredService<IPrintJobMonitor>();
        var printerId = SampleHelpers.ParsePrinterId(identifierText);
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

        // A raw channel has no job queue, so the job is already reported complete.
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

    // RequirePassthrough: the bytes must reach the device unchanged, or the manager throws.
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
            // The refusal is the guarantee working: no channel of this printer keeps it.
            Console.WriteLine("The manager sent nothing, because no channel of this printer");
            Console.WriteLine("sends the bytes unchanged. A filtering channel could rasterise");
            Console.WriteLine("the ZPL and print the command source instead of the label.");
            Console.WriteLine("A raw TCP channel keeps the promise, and so does the Windows spooler.");
            Console.WriteLine("CUPS does not: only a raw CUPS queue passes the bytes on, and CUPS");
            Console.WriteLine("reports nothing that tells such a queue apart, so it is refused here.");
            Console.WriteLine("Send without RequirePassthrough only when the printer reads the format you send.");
            Console.WriteLine($"Reason: {exception.Message}");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{printerId} was not found; nothing was sent.");
        }
    }

    // A printer that cannot report status must not break the rest of the listing.
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
