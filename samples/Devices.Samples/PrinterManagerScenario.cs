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

        foreach (var printer in printers)
        {
            Console.WriteLine($"- {printer.Info.Name} — {printer.Id.Value} ({printer.Endpoint}) [{printer.Source}]");
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

    internal static async Task SendAsync(ServiceProvider provider, string directory, string host, string fileName)
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
        var printerId = PrinterId.FromNetwork(host);
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        await SeedFromHostAsync(manager, host).ConfigureAwait(false);

        PrintJobInfo submitted;
        try
        {
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
                new PrintOptions { JobName = safeFileName },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{host} did not answer on the raw port; nothing was sent.");
            return;
        }

        Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
    }

    internal static async Task WatchAsync(ServiceProvider provider, string directory, string host, string fileName)
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
        var printerId = PrinterId.FromNetwork(host);
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        await SeedFromHostAsync(manager, host).ConfigureAwait(false);

        PrintJobInfo submitted;
        try
        {
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
                new PrintOptions { JobName = safeFileName },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"{host} did not answer on the raw port; nothing was sent.");
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
            printerId, submitted.JobId, new PrintJobMonitorOptions(), CancellationToken.None).ConfigureAwait(false))
        {
            Console.WriteLine($"  {SampleHelpers.DescribeJobReading(reading)}");
        }
    }

    // RequirePassthrough tells the manager the bytes must reach the device unchanged. A
    // printer with only a filtering channel cannot promise that, so the manager throws.
    internal static async Task SendZplAsync(ServiceProvider provider, string host)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        var payload = PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

        await SeedFromHostAsync(manager, host).ConfigureAwait(false);

        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));
        try
        {
            var submitted = await manager.PrintAsync(
                PrinterId.FromNetwork(host),
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
            Console.WriteLine($"{host} did not answer on the raw port; nothing was sent.");
        }
    }

    // PrintAsync resolves only from the cache or a fresh mDNS-plus-spooler discovery, and a
    // label printer given by address often advertises on neither. A targeted probe of that one
    // host is how PrinterManagerOptions.Probe closes the gap.
    private static async Task SeedFromHostAsync(IPrinterManager manager, string host)
    {
        PrinterManagerOptions targeted = new()
        {
            // The caller named the host, so neither the browse nor the spooler can help.
            IncludeSpooler = false,
            IncludeMdns = false,
            Probe = new NetworkPrinterDiscoveryOptions
            {
                Hosts = [host],
                ConnectTimeout = TimeSpan.FromSeconds(2),
            },
        };
        _ = await manager.DiscoverAsync(targeted, CancellationToken.None).ConfigureAwait(false);
    }
}
