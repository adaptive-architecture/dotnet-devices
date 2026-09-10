using System.Text;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples;

// A menu for hardware tests. One discovery serves every flow, so a test session
// does not pay the browse cost again, and nobody copies an identifier by hand.
internal static class InteractiveScenario
{
    internal static async Task RunAsync(ServiceProvider provider, string printFilesDirectory)
    {
        var devices = await DiscoverAsync(provider).ConfigureAwait(false);
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Interactive test menu");
            Console.WriteLine("  0) Quit");
            Console.WriteLine("  1) Discover printers again");
            Console.WriteLine("  2) Print a file through a queue (spooler or IPP), and watch the job");
            Console.WriteLine("  3) Print a file raw, through a passthrough or spooler channel");
            Console.WriteLine("  4) Show the status of a printer");
            Console.WriteLine("  5) Show the capabilities of a printer");
            Console.Write("Choice: ");

            // A null line means the input closed, so the loop must end.
            var choice = Console.ReadLine();
            if (choice is null)
            {
                return;
            }

            switch (choice.Trim())
            {
                case "0":
                    return;
                case "1":
                    devices = await DiscoverAsync(provider).ConfigureAwait(false);
                    break;
                case "2":
                    await QueuePrintAsync(provider, devices, printFilesDirectory).ConfigureAwait(false);
                    break;
                case "3":
                    await RawPrintAsync(provider, devices, printFilesDirectory).ConfigureAwait(false);
                    break;
                case "4":
                    await StatusAsync(provider, devices).ConfigureAwait(false);
                    break;
                case "5":
                    ShowCapabilities(devices);
                    break;
                default:
                    Console.WriteLine("Select a number from the menu.");
                    break;
            }
        }
    }

    private static async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(ServiceProvider provider)
    {
        var devices = await PrinterManagerScenario.BrowseAsync(provider).ConfigureAwait(false);
        if (devices.Count == 0)
        {
            devices = await PrinterManagerScenario.ProbeAsync(provider).ConfigureAwait(false);
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No printers found.");
            return devices;
        }

        Console.WriteLine($"Found {devices.Count} printer(s):");
        for (var index = 0; index < devices.Count; index++)
        {
            Console.WriteLine($"  {index + 1}) {Describe(devices[index])}");
        }

        return devices;
    }

    // One line for each printer: enough to select it, and to see what it can do.
    private static string Describe(PrinterDevice device)
    {
        var transports = String.Join("/", device.ChannelsByTransport.Keys);
        var traits = device.HasJobQueue ? "job queue" : "no job queue";
        traits += device.GivesPassthrough ? ", passthrough" : ", may convert";
        return $"{device.Details.Name} — {device.Id} [{transports}; {traits}]";
    }

    // Every channel with a job queue: the spooler, and IPP or IPPS. Both carry the whole
    // job template, so both can take a rotation and a scaling mode.
    private static async Task QueuePrintAsync(ServiceProvider provider, IReadOnlyList<PrinterDevice> devices, string directory)
    {
        var channels = Collect(devices, static channel => channel.HasJobQueue);
        if (channels.Count == 0)
        {
            Console.WriteLine("No printer of the list has a channel with a job queue.");
            return;
        }

        var selected = SampleHelpers.Choose("Queues:", Label(channels));
        if (selected < 0)
        {
            return;
        }

        var fileName = ChooseFile(directory);
        if (fileName is null)
        {
            return;
        }

        var target = channels[selected];
        var contentType = SampleHelpers.GetContentType(fileName);
        PrintOptions options = new() { JobName = fileName };
        if (SampleHelpers.IsImage(contentType))
        {
            var colorMode = ChooseColorMode();
            if (colorMode is null)
            {
                return;
            }

            options.ColorMode = colorMode;
        }

        // Only what the printer reported is offered. A printer that reported nothing is
        // not asked, because the sample would be inventing the choice.
        var configuration = target.Printer.Configuration;
        if (!TryChoose("Rotation:", configuration?.SupportedOrientations ?? [], out var orientation))
        {
            return;
        }

        if (!TryChoose("Scaling:", configuration?.SupportedScalings ?? [], out var scaling))
        {
            return;
        }

        options.Orientation = orientation;
        options.Scaling = scaling;

        if (!SampleHelpers.Confirm($"Send '{fileName}' as {contentType} to {target.Printer.Id}?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await SendAndWatchAsync(provider, target.Printer.Id, directory, fileName, contentType, options).ConfigureAwait(false);
    }

    // "Printer default" is the first entry, so an operator can leave the option unset.
    // A cancel returns false, and the caller sends nothing.
    private static bool TryChoose<T>(string title, IReadOnlyList<T> supported, out T? chosen)
        where T : struct
    {
        chosen = null;
        if (supported.Count == 0)
        {
            return true;
        }

        List<string> labels = ["Printer default"];
        foreach (var value in supported)
        {
            labels.Add(value.ToString());
        }

        var selected = SampleHelpers.Choose(title, labels);
        if (selected < 0)
        {
            return false;
        }

        if (selected > 0)
        {
            chosen = supported[selected - 1];
        }

        return true;
    }

    // The queue renders the image, so the colour mode is a choice the caller makes.
    // A printer that reports no colour support drops the option, and says so.
    private static PrintColorMode? ChooseColorMode()
    {
        var selected = SampleHelpers.Choose("Colour mode:", ["Colour", "Grayscale"]);
        if (selected < 0)
        {
            return null;
        }

        return selected == 0 ? PrintColorMode.Color : PrintColorMode.Monochrome;
    }

    // The true content type matters. It is what makes the IPP channel negotiate the
    // format, and send a label language as application/vnd.cups-raw instead of text.
    private static async Task SendAndWatchAsync(
        ServiceProvider provider, PrinterId printerId, string directory, string fileName, string contentType, PrintOptions options)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        var monitor = provider.GetRequiredService<IPrintJobMonitor>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, fileName), timeoutSource.Token).ConfigureAwait(false);

        PrintJobInfo submitted;
        try
        {
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, contentType),
                options,
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine($"Nothing was sent: {exception.Message}");
            return;
        }

        Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
        if (submitted.DroppedOptions.Count > 0)
        {
            Console.WriteLine($"  dropped options: {String.Join(", ", submitted.DroppedOptions)}");
        }

        if (submitted.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
        {
            return;
        }

        await foreach (var reading in monitor.WatchJobAsync(
            printerId, submitted.JobId, new PrintJobMonitorOptions(), timeoutSource.Token).ConfigureAwait(false))
        {
            Console.WriteLine($"  {SampleHelpers.DescribeJobReading(reading)}");
        }
    }

    // A raw channel writes the bytes to the device without a change. The device must read
    // the format itself, because nothing on the way converts it. The spooler is offered
    // beside it: on Windows it uses the RAW data type and keeps the same promise, and on
    // CUPS it keeps the bytes only when the queue itself was made raw.
    private static async Task RawPrintAsync(ServiceProvider provider, IReadOnlyList<PrinterDevice> devices, string directory)
    {
        var channels = Collect(
            devices,
            static channel => channel.GivesPassthrough || channel.Endpoint.Scheme == PrinterScheme.Spooler);
        if (channels.Count == 0)
        {
            Console.WriteLine("No printer of the list has a raw channel or a spooler queue.");
            return;
        }

        var selected = SampleHelpers.Choose("Raw-capable channels:", Label(channels));
        if (selected < 0)
        {
            return;
        }

        var fileName = ChooseFile(directory);
        if (fileName is null)
        {
            return;
        }

        var target = channels[selected];
        var contentType = SampleHelpers.GetContentType(fileName);
        if (SampleHelpers.IsImage(contentType))
        {
            // A raw send carries no job template, so the colour is whatever the file holds.
            Console.WriteLine("A raw send carries no colour, rotation or scaling option.");
            Console.WriteLine("The printer renders the file as it is.");
        }

        WriteRawWarning(target);
        if (!ConfirmRaw(target.Device, fileName, contentType))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await SendRawAsync(provider, target, directory, fileName, contentType).ConfigureAwait(false);
    }

    // The promise differs by channel, and the operator must know which one they get.
    private static void WriteRawWarning(Channel target)
    {
        if (target.Printer.GivesPassthrough)
        {
            return;
        }

        Console.WriteLine($"{target.Printer.Id} is a CUPS queue, which gives no promise to keep the bytes.");
        Console.WriteLine("A queue made with 'lpadmin -m raw' passes them on; a queue with a driver");
        Console.WriteLine("converts the job instead. CUPS reports nothing that tells the two apart, so");
        Console.WriteLine("the send below asks for no guarantee. A printer language is still submitted");
        Console.WriteLine("as application/vnd.cups-raw, which is what a raw queue needs.");
    }

    // The printer reads the bytes itself, so ask it first whether it knows the format.
    private static bool ConfirmRaw(PrinterDevice device, string fileName, string contentType)
    {
        var accepts = SampleHelpers.Accepts(device, contentType);
        if (accepts is null)
        {
            Console.WriteLine($"{device.Details.Name} reports no format and no command set, so it is not");
            Console.WriteLine($"known whether it reads {contentType}. A printer that reads nothing prints nothing.");
            return SampleHelpers.Confirm($"Send '{fileName}' anyway?");
        }

        if (accepts == false)
        {
            Console.WriteLine($"{device.Details.Name} does not report {contentType}. A raw send is not");
            Console.WriteLine("converted, so the printer will probably print nothing, or print the source as text.");
            Console.WriteLine("A PDF needs a PDF interpreter in the firmware; a label needs the label language.");
            return SampleHelpers.Confirm($"Send '{fileName}' anyway, to see what the printer does?");
        }

        Console.WriteLine($"{device.Details.Name} reports {contentType}.");
        return SampleHelpers.Confirm($"Send '{fileName}' unchanged to {device.Id}?");
    }

    // RequirePassthrough is asked for only where the channel can keep it. A CUPS queue
    // would be refused by the manager, so the send states the risk instead of the promise.
    private static async Task SendRawAsync(
        ServiceProvider provider, Channel target, string directory, string fileName, string contentType)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(30));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, fileName), timeoutSource.Token).ConfigureAwait(false);

        try
        {
            var submitted = await manager.PrintAsync(
                target.Printer.Id,
                PrinterPayload.FromBytes(bytes, contentType),
                new PrintOptions { RequirePassthrough = target.Printer.GivesPassthrough, JobName = fileName },
                timeoutSource.Token).ConfigureAwait(false);
            Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
            if (submitted.DroppedOptions.Count > 0)
            {
                Console.WriteLine($"  dropped options: {String.Join(", ", submitted.DroppedOptions)}");
            }

            if (!target.Printer.HasJobQueue)
            {
                Console.WriteLine("A raw channel has no job queue, so there is no progress to watch.");
            }

            Console.WriteLine("Look at the printer to see the result.");
        }
        catch (NotSupportedException exception)
        {
            // The refusal is the guarantee working: no channel of this printer keeps it.
            Console.WriteLine("Nothing was sent, because no channel of this printer sends the");
            Console.WriteLine("bytes unchanged. A filtering channel could print the source instead.");
            Console.WriteLine($"Reason: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine($"Nothing was sent: {exception.Message}");
        }
    }

    private static async Task StatusAsync(ServiceProvider provider, IReadOnlyList<PrinterDevice> devices)
    {
        var selected = SampleHelpers.Choose("Printers:", Select(devices));
        if (selected < 0)
        {
            return;
        }

        var manager = provider.GetRequiredService<IPrinterManager>();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));
        try
        {
            var status = await manager.GetStatusAsync(devices[selected].Id, timeoutSource.Token).ConfigureAwait(false);
            StringBuilder line = new($"{status.State}, accepting jobs: {status.IsAcceptingJobs}");
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
            Console.WriteLine($"  {line}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  Status unavailable: {exception.Message}");
        }
    }

    // What the printer says it can read is what decides whether a raw send works.
    private static void ShowCapabilities(IReadOnlyList<PrinterDevice> devices)
    {
        var selected = SampleHelpers.Choose("Printers:", Select(devices));
        if (selected < 0)
        {
            return;
        }

        var device = devices[selected];
        Console.WriteLine($"  device      : {device.Key}");
        WriteList("command sets", device.Details.CommandSets);
        foreach (var channel in device.Channels)
        {
            Console.WriteLine($"  {channel.Endpoint.Scheme,-8}: {channel.Id}");
            Console.WriteLine($"      options : {channel.SupportedOptions}");
            WriteList("      formats", channel.Configuration?.SupportedDocumentFormats ?? []);
            WriteList("      rotation", Names(channel.Configuration?.SupportedOrientations ?? []));
            WriteList("      scaling", Names(channel.Configuration?.SupportedScalings ?? []));
        }
    }

    private static IReadOnlyList<string> Names<T>(IReadOnlyList<T> values)
        where T : struct
    {
        List<string> names = [];
        foreach (var value in values)
        {
            names.Add(value.ToString());
        }

        return names;
    }

    private static void WriteList(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine(values.Count == 0
            ? $"  {label}: none reported"
            : $"  {label}: {String.Join(", ", values)}");
    }

    // One channel of one device. The device carries the identity and the command sets;
    // the channel carries the identifier, the capabilities and the promises.
    private sealed record Channel(PrinterDevice Device, DiscoveredPrinter Printer);

    private static List<Channel> Collect(IReadOnlyList<PrinterDevice> devices, Func<DiscoveredPrinter, bool> keep)
    {
        List<Channel> channels = [];
        foreach (var device in devices)
        {
            foreach (var channel in device.Channels)
            {
                if (keep(channel))
                {
                    channels.Add(new Channel(device, channel));
                }
            }
        }

        return channels;
    }

    private static IReadOnlyList<string> Label(IReadOnlyList<Channel> channels)
    {
        List<string> labels = [];
        foreach (var channel in channels)
        {
            var promise = channel.Printer.GivesPassthrough ? "sends the bytes unchanged" : "may convert the job";
            labels.Add($"{channel.Device.Details.Name} — {channel.Printer.Id} [{promise}]");
        }

        return labels;
    }

    private static IReadOnlyList<string> Select(IReadOnlyList<PrinterDevice> devices)
    {
        List<string> labels = [];
        foreach (var device in devices)
        {
            labels.Add(Describe(device));
        }

        return labels;
    }

    private static string ChooseFile(string directory)
    {
        var files = SampleHelpers.GetPrintFiles(directory).Where(SampleHelpers.CanPrint).ToList();
        var selected = SampleHelpers.Choose("Files:", files);
        return selected < 0 ? null : files[selected];
    }
}
