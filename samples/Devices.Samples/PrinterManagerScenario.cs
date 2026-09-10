using System.Globalization;
using System.Linq;
using System.Text;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples;

// The recommended path: IPrinterManager covers discovery, printing and job progress.
internal static class PrinterManagerScenario
{
    internal static async Task DiscoverAsync(ServiceProvider provider)
    {
        var devices = await BrowseAsync(provider).ConfigureAwait(false);
        if (devices.Count == 0)
        {
            // Nothing advertised itself, so probe every local address.
            devices = await ProbeAsync(provider).ConfigureAwait(false);
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No printers found on the local network.");
            return;
        }

        var manager = provider.GetRequiredService<IPrinterManager>();
        foreach (var device in devices)
        {
            Describe(device);
            await PrintManagerStatusAsync(manager, device.Id).ConfigureAwait(false);
        }
    }

    // One device, then the channels that reach it, grouped by transport.
    private static void Describe(PrinterDevice device)
    {
        var details = device.Details;
        Console.WriteLine($"- {details.Name} — {device.Id}");
        Console.WriteLine($"    device      : {device.Key}");
        var makeAndModel = String.Join(' ', new[] { details.Manufacturer, details.Model }.Where(static part => !String.IsNullOrWhiteSpace(part)));
        if (makeAndModel.Length > 0)
        {
            Console.WriteLine($"    make/model  : {makeAndModel}");
        }

        if (details.SerialNumber is not null)
        {
            Console.WriteLine($"    serial      : {details.SerialNumber}");
        }

        if (details.Location is not null)
        {
            Console.WriteLine($"    location    : {details.Location}");
        }

        Console.WriteLine($"    found by    : {String.Join(", ", details.ContributedBy)}");
        if (details.StatusSources.Count > 0)
        {
            Console.WriteLine($"    answered by : {String.Join(", ", details.StatusSources)}");
        }

        foreach ((var transport, var channels) in device.ChannelsByTransport)
        {
            foreach (var channel in channels)
            {
                Console.WriteLine($"    {transport,-8}: {channel.Id} ({channel.Endpoint})");
                Console.WriteLine($"        options : {channel.SupportedOptions}");
                Console.WriteLine($"        {(channel.GivesPassthrough ? "sends the bytes unchanged" : "may convert the job")}"
                    + $", {(channel.HasJobQueue ? "job can be watched" : "no job queue")}");
                if (channel.Configuration is not null)
                {
                    Describe(channel.Configuration);
                }
            }
        }
    }

    private static void Describe(PrinterConfiguration configuration)
    {
        Console.WriteLine($"        duplex  : {Describe(configuration.SupportsDuplex)}, colour: {Describe(configuration.SupportsColor)}"
            + $", ranges: {Describe(configuration.SupportsPageRanges)}");
        if (configuration.SupportedResolutionsDpi.Count > 0)
        {
            Console.WriteLine($"        dpi     : {String.Join(", ", configuration.SupportedResolutionsDpi)}");
        }

        WriteList("media", configuration.Media.Select(Describe));
        WriteList("trays", configuration.MediaSources.Select(Describe));
        WriteList("types", configuration.MediaTypes);
        WriteList("bins", configuration.OutputBins);
        WriteList("quality", configuration.Qualities.Select(static quality => quality.ToString()));
        WriteList("n-up", configuration.NumberUpValues.Select(static pages => pages.ToString(CultureInfo.InvariantCulture)));
        WriteList("formats", configuration.SupportedDocumentFormats);
        WriteDefaults(configuration);
    }

    // What the printer does when a job asks for nothing.
    private static void WriteDefaults(PrinterConfiguration configuration)
    {
        List<string> defaults = [];
        AddDefault(defaults, "media", configuration.DefaultMediaSize);
        AddDefault(defaults, "tray", configuration.DefaultMediaSource);
        AddDefault(defaults, "orientation", configuration.DefaultOrientation?.ToString());
        AddDefault(defaults, "dpi", configuration.DefaultResolutionDpi?.ToString(CultureInfo.InvariantCulture));
        WriteList("default", defaults);
    }

    private static void AddDefault(List<string> defaults, string name, string value)
    {
        if (!String.IsNullOrWhiteSpace(value))
        {
            defaults.Add($"{name}={value}");
        }
    }

    // The Windows spooler is the only channel that reports a device mode number.
    private static string Describe(PrinterMedia media) =>
        media.WindowsPaperNumber is int number ? $"{media.Name} ({number})" : media.Name;

    private static string Describe(PrinterMediaSource source) =>
        source.WindowsBinNumber is int number ? $"{source.Name} ({number})" : source.Name;

    // An empty list means the printer reported nothing, so the line is left out.
    private static void WriteList(string label, IEnumerable<string> values)
    {
        var text = String.Join(", ", values);
        if (!String.IsNullOrEmpty(text))
        {
            Console.WriteLine($"        {label,-8}: {text}");
        }
    }

    // A capability the printer did not report is not a capability it denied.
    private static string Describe(bool? value) => value is null ? "not reported" : value.Value ? "yes" : "no";

    internal static async Task<IReadOnlyList<PrinterDevice>> BrowseAsync(ServiceProvider provider)
    {
        Console.WriteLine("Asking the local network for printers over mDNS, and checking the print spooler...");
        var manager = provider.GetRequiredService<IPrinterManager>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(30));
        try
        {
            // Both reads are opt-in because each costs one request per channel. They are
            // what merges the channels of one printer and fills in its capabilities.
            PrinterManagerOptions options = new() { ReadIdentity = true, ReadCapabilities = true };
            return await manager.DiscoverAsync(options, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"The browse failed: {exception.Message}");
            return [];
        }
    }

    internal static async Task<IReadOnlyList<PrinterDevice>> ProbeAsync(ServiceProvider provider)
    {
        var hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts();
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
            ReadIdentity = true,
            ReadCapabilities = true,
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
        var printerId = PrinterId.ParseOrRaw(identifierText);
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
        var printerId = PrinterId.ParseOrRaw(identifierText);
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
        var printerId = PrinterId.ParseOrRaw(identifierText);
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
