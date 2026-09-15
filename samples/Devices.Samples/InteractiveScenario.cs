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
            Console.WriteLine("  6) Run the whole test sequence");
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
                case "6":
                    devices = await TestRunAsync(provider, printFilesDirectory).ConfigureAwait(false);
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

        // A queue job is converted on the way, except on a Windows spooler image path
        // that the driver rasterises, so a format the channel denies prints wrong or
        // blank instead of nothing. Ask before wasting the paper.
        var accepts = target.Device.Accepts(target.Printer, contentType);
        if (accepts == false)
        {
            SampleHelpers.WriteWarning($"{target.Printer.Id} does not report {contentType}. The page may come out blank.");
            WriteAdvertised(target.Printer, String.Empty);
            if (!SampleHelpers.Confirm($"Send '{fileName}' anyway, to see what the printer does?"))
            {
                Console.WriteLine("Cancelled; nothing was sent.");
                return;
            }
        }
        else if (accepts is null)
        {
            SampleHelpers.WriteWarning(
                $"{target.Printer.Id} reports no format, so it is not known whether it reads {contentType}.");
        }

        if (!SampleHelpers.Confirm($"Send '{fileName}' as {contentType} to {target.Printer.Id}?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        _ = await SendAndWatchAsync(provider, target.Printer.Id, directory, fileName, contentType, options).ConfigureAwait(false);
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
    private static async Task<JobOutcome> SendAndWatchAsync(
        ServiceProvider provider, PrinterId printerId, string directory, string fileName, string contentType, PrintOptions options)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        var monitor = provider.GetRequiredService<IPrintJobMonitor>();

        // This budget covers reading the file and handing the job over, and nothing else.
        // It deliberately does not cover the watch: a printer that is still printing has
        // not failed, and a deadline is not a cancellation.
        using CancellationTokenSource submitSource = new(TimeSpan.FromMinutes(2));

        PrintJobInfo submitted;
        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, fileName), submitSource.Token).ConfigureAwait(false);
            submitted = await manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, contentType),
                options,
                submitSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or TimeoutException or IOException)
        {
            Console.WriteLine($"Nothing was sent: {exception.Message}");
            return new JobOutcome(fileName, JobResult.Failed, exception.Message);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Nothing was sent: the printer did not take the job in two minutes.");
            return new JobOutcome(fileName, JobResult.Failed, "the submission timed out");
        }

        Console.WriteLine($"Job {submitted.JobId} submitted ({submitted.State}).");
        if (submitted.DroppedOptions.Count > 0)
        {
            Console.WriteLine($"  dropped options: {String.Join(", ", submitted.DroppedOptions)}");
        }

        if (submitted.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
        {
            return Classify(fileName, submitted);
        }

        // The watch is bounded by lost progress, not by a wall clock. A printer that woke
        // from sleep may take minutes over the first page and then print steadily; every
        // page resets the idle timeout, so only a job that has really stopped ends the
        // watch. The token stays None because this sample has no cancellation of its own:
        // a real caller passes its own here, never a deadline.
        PrintJobMonitorOptions watch = new() { IdleTimeout = TimeSpan.FromMinutes(2) };
        var last = submitted;
        try
        {
            await foreach (var reading in monitor.WatchJobAsync(
                printerId, submitted.JobId, watch, CancellationToken.None).ConfigureAwait(false))
            {
                last = reading;
                Console.WriteLine($"  {SampleHelpers.DescribeJobReading(reading)}");
            }
        }
        catch (Exception exception)
        {
            // Losing sight of a job does not undo it: the printer may well finish.
            Console.WriteLine($"  the watch stopped: {exception.Message}");
            return new JobOutcome(fileName, JobResult.StillPrinting, exception.Message);
        }

        if (last.State is not (PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled))
        {
            Console.WriteLine($"  nothing changed for {watch.IdleTimeout.Value.TotalMinutes:0.#} minute(s); the job may still be printing.");
        }

        return Classify(fileName, last);
    }

    private static JobOutcome Classify(string fileName, PrintJobInfo job) => job.State switch
    {
        PrintJobState.Completed => new JobOutcome(fileName, JobResult.Printed, $"job {job.JobId}"),
        PrintJobState.Failed => new JobOutcome(fileName, JobResult.Failed, job.Detail ?? "the printer reported a failure"),
        PrintJobState.Canceled => new JobOutcome(fileName, JobResult.Failed, "the job was cancelled"),
        _ => new JobOutcome(fileName, JobResult.StillPrinting, $"last seen {job.State}"),
    };

    // What became of one job of the sequence. The run reports these at the end, because a
    // sequence that prints five documents scrolls the early ones off the screen.
    private sealed record JobOutcome(string What, JobResult Result, string Detail);

    private enum JobResult
    {
        Printed,
        StillPrinting,
        Skipped,
        Failed,
    }

    // A fixed sequence for a hardware test: the same jobs, in the same order, every time,
    // so two runs can be compared and a change in the output has one cause. It returns the
    // devices it found, so the menu keeps them.
    internal static async Task<IReadOnlyList<PrinterDevice>> TestRunAsync(ServiceProvider provider, string directory)
    {
        var devices = await DiscoverAsync(provider).ConfigureAwait(false);
        if (devices.Count == 0)
        {
            return devices;
        }

        var manager = provider.GetRequiredService<IPrinterManager>();
        Console.WriteLine();
        Console.WriteLine("== Printers ==");
        foreach (var device in devices)
        {
            // One block per physical device. The status is read first so it can sit on the
            // header line, beside the name it belongs to.
            WriteDevice(device, await ReadStatusAsync(manager, device).ConfigureAwait(false));
        }

        List<JobOutcome> outcomes = [];
        await RunQueueJobsAsync(provider, devices, directory, outcomes).ConfigureAwait(false);
        await RunRawJobsAsync(provider, devices, directory, outcomes).ConfigureAwait(false);
        WriteSummary(outcomes);
        return devices;
    }

    // Five queue jobs and three raw files across every channel scroll the early results off
    // the screen, so the run says at the end what became of each one.
    private static void WriteSummary(List<JobOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("== Summary ==");
        foreach (var group in new[] { JobResult.Printed, JobResult.StillPrinting, JobResult.Failed, JobResult.Skipped })
        {
            var matching = outcomes.Where(outcome => outcome.Result == group).ToList();
            if (matching.Count == 0)
            {
                continue;
            }

            Console.WriteLine($"{Describe(group)} ({matching.Count}):");
            foreach (var outcome in matching)
            {
                Console.WriteLine($"  {outcome.What} — {outcome.Detail}");
            }
        }

        var unfinished = outcomes.Count(static outcome => outcome.Result == JobResult.StillPrinting);
        if (unfinished > 0)
        {
            // Not a failure. The watch gave up on the job; the printer did not.
            SampleHelpers.WriteWarning(
                $"{unfinished} job(s) were still going when the watch ended. Look at the printer for the result.");
        }
    }

    private static string Describe(JobResult result) => result switch
    {
        JobResult.Printed => "Printed",
        JobResult.StillPrinting => "Still printing when the watch ended",
        JobResult.Failed => "Failed",
        _ => "Skipped",
    };

    // The five jobs the test sequence sends through one queue. Each one changes exactly
    // one thing from the one before it, so a wrong page names its own option.
    private static IReadOnlyList<TestJob> BuildJobs() =>
    [
        new TestJob("document.pdf", "a PDF with the printer defaults", new PrintOptions()),
        new TestJob(
            "image.png",
            "a PNG in colour, at its own size",
            new PrintOptions { ColorMode = PrintColorMode.Color, Scaling = PrintScaling.None }),
        new TestJob(
            "image.png",
            "a PNG in grayscale, rotated 90 degrees",
            new PrintOptions { ColorMode = PrintColorMode.Monochrome, Orientation = PrintOrientation.Landscape }),
        new TestJob(
            "photo.jpeg",
            "a JPEG rotated 180 degrees",
            new PrintOptions { Orientation = PrintOrientation.ReversePortrait }),
        new TestJob(
            "photo.jpeg",
            "a JPEG in grayscale, filling the media",
            new PrintOptions { ColorMode = PrintColorMode.Monochrome, Scaling = PrintScaling.Fill }),
    ];

    private static async Task RunQueueJobsAsync(
        ServiceProvider provider, IReadOnlyList<PrinterDevice> devices, string directory, List<JobOutcome> outcomes)
    {
        var channels = Collect(devices, static channel => channel.Endpoint.Scheme == PrinterScheme.Spooler);
        if (channels.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No printer of the list has a spooler queue, so the queue jobs are skipped.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("== Queue jobs ==");
        var selected = SampleHelpers.Choose("Spooler queue to print with:", Label(channels));
        if (selected < 0)
        {
            return;
        }

        var target = channels[selected];
        var jobs = BuildJobs();
        Console.WriteLine($"The sequence prints {jobs.Count} documents:");
        foreach (var job in jobs)
        {
            Console.WriteLine($"  {job.FileName}: {job.Description}");
        }

        if (!SampleHelpers.Confirm($"Print all {jobs.Count} on {target.Printer.Id}?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        foreach (var job in jobs)
        {
            outcomes.Add(await RunJobAsync(provider, target, directory, job).ConfigureAwait(false));
        }
    }

    // Never throws. One job that fails must not cost the jobs after it, nor the raw
    // sequence that follows: the point of the run is to compare them all.
    private static async Task<JobOutcome> RunJobAsync(ServiceProvider provider, Channel target, string directory, TestJob job)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {job.FileName}: {job.Description}");
        if (!File.Exists(Path.Combine(directory, job.FileName)))
        {
            Console.WriteLine($"   '{job.FileName}' is not in PrintFiles, so this job is skipped.");
            return new JobOutcome(job.FileName, JobResult.Skipped, "the file is not in PrintFiles");
        }

        WarnUnreported(target.Printer, job.Options);
        var contentType = SampleHelpers.GetContentType(job.FileName);
        var accepts = target.Device.Accepts(target.Printer, contentType);
        if (accepts == false)
        {
            SampleHelpers.WriteWarning($"   skipped {job.FileName}: this queue does not read {contentType}.");
            WriteAdvertised(target.Printer, "   ");
            return new JobOutcome(job.FileName, JobResult.Skipped, $"the queue does not read {contentType}");
        }

        if (accepts is null)
        {
            SampleHelpers.WriteWarning(
                $"   {job.FileName}: this queue reports no format, so it is not known whether it reads {contentType}.");
        }

        job.Options.JobName = $"{job.FileName} — {job.Description}";
        try
        {
            return await SendAndWatchAsync(
                provider,
                target.Printer.Id,
                directory,
                job.FileName,
                contentType,
                job.Options).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SampleHelpers.WriteWarning($"   {job.FileName} failed: {exception.Message}");
            return new JobOutcome(job.FileName, JobResult.Failed, exception.Message);
        }
    }

    // The job is still sent: the printer is the authority, and a value it did not list is
    // not a value it refused. Saying so first makes an odd page easy to explain.
    private static void WarnUnreported(DiscoveredPrinter channel, PrintOptions options)
    {
        var configuration = channel.Configuration;
        if (configuration is null)
        {
            return;
        }

        if (options.Orientation is PrintOrientation orientation
            && configuration.SupportedOrientations.Count > 0
            && !configuration.SupportedOrientations.Contains(orientation))
        {
            Console.WriteLine($"   note: the printer did not report the rotation {orientation}. It is sent anyway.");
        }

        if (options.Scaling is PrintScaling scaling
            && configuration.SupportedScalings.Count > 0
            && !configuration.SupportedScalings.Contains(scaling))
        {
            Console.WriteLine($"   note: the printer did not report the scaling {scaling}. It is sent anyway.");
        }
    }

    // The raw files the test sequence offers. A label language belongs here beside the
    // image, because the raw path is what a label printer needs.
    private static readonly string[] RawFiles = ["photo.jpeg", "label.zpl", "label.epl"];

    // Every raw file to every channel that can take it unchanged, so the raw path is
    // compared across the printers in one step. A channel that cannot read a format is
    // not sent it: a raw send is not converted, so the paper would only be wasted.
    private static async Task RunRawJobsAsync(
        ServiceProvider provider, IReadOnlyList<PrinterDevice> devices, string directory, List<JobOutcome> outcomes)
    {
        var channels = Collect(
            devices,
            static channel => channel.GivesPassthrough || channel.Endpoint.Scheme == PrinterScheme.Spooler);
        List<string> files = [];
        foreach (var name in RawFiles)
        {
            if (File.Exists(Path.Combine(directory, name)))
            {
                files.Add(name);
            }
        }

        if (channels.Count == 0 || files.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No raw-capable channel, or none of the raw files, so the raw jobs are skipped.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("== Raw jobs ==");
        Console.WriteLine($"Each of {String.Join(", ", files)} goes unchanged to each of these channels,");
        Console.WriteLine("and is skipped where the channel does not read that format:");
        foreach (var label in Label(channels))
        {
            Console.WriteLine($"  {label}");
        }

        if (!SampleHelpers.Confirm($"Send the raw jobs to all {channels.Count} channel(s)?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        foreach (var channel in channels)
        {
            Console.WriteLine();
            Console.WriteLine($"-- {channel.Printer.Id}");
            foreach (var fileName in files)
            {
                outcomes.Add(await TryRawAsync(provider, channel, directory, fileName).ConfigureAwait(false));
            }
        }
    }

    // A format the channel reports it does not read is skipped, not sent. A channel that
    // reports nothing has denied nothing, so that job is still sent, with a warning.
    private static async Task<JobOutcome> TryRawAsync(ServiceProvider provider, Channel channel, string directory, string fileName)
    {
        var contentType = SampleHelpers.GetContentType(fileName);
        var accepts = channel.Device.Accepts(channel.Printer, contentType);
        if (accepts == false)
        {
            SampleHelpers.WriteWarning($"   skipped {fileName}: this channel does not read {contentType}.");
            WriteAdvertised(channel.Printer, "   ");
            return new JobOutcome($"{fileName} raw to {channel.Printer.Id}", JobResult.Skipped, $"the channel does not read {contentType}");
        }

        if (accepts is null)
        {
            SampleHelpers.WriteWarning(
                $"   {fileName}: this channel reports no format, so it is not known whether it reads {contentType}.");
        }
        else
        {
            Console.WriteLine($"   {fileName}: this channel reads {contentType}.");
        }

        return await SendRawAsync(provider, channel, directory, fileName, contentType).ConfigureAwait(false);
    }

    // One job of the test sequence. The options are built once and used once.
    private sealed record TestJob(string FileName, string Description, PrintOptions Options);

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
        if (!ConfirmRaw(target, fileName, contentType))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        _ = await SendRawAsync(provider, target, directory, fileName, contentType).ConfigureAwait(false);
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
        Console.WriteLine("converts the job instead. CUPS reports nothing that tells the two apart.");
        Console.WriteLine("A printer language is still submitted as application/vnd.cups-raw, which is");
        Console.WriteLine("what a raw queue needs.");
    }

    // The printer reads the bytes itself, so ask it first whether it knows the format.
    private static bool ConfirmRaw(Channel target, string fileName, string contentType)
    {
        var accepts = target.Device.Accepts(target.Printer, contentType);
        if (accepts is null)
        {
            SampleHelpers.WriteWarning($"{target.Printer.Id} reports no format and no command set, so it is");
            SampleHelpers.WriteWarning($"not known whether it reads {contentType}. A printer that reads nothing prints nothing.");
            return SampleHelpers.Confirm($"Send '{fileName}' anyway?");
        }

        if (accepts == false)
        {
            SampleHelpers.WriteWarning($"{target.Printer.Id} does not report {contentType}. A raw send is not");
            SampleHelpers.WriteWarning("converted, so the printer will probably print nothing, or print the source as text.");
            SampleHelpers.WriteWarning("A PDF needs a PDF interpreter in the firmware; a label needs the label language.");
            WriteAdvertised(target.Printer, String.Empty);
            return SampleHelpers.Confirm($"Send '{fileName}' anyway, to see what the printer does?");
        }

        Console.WriteLine($"{target.Printer.Id} reports {contentType}.");
        return SampleHelpers.Confirm($"Send '{fileName}' unchanged to {target.Printer.Id}?");
    }

    // The list the channel does read is the useful next step, so it is printed with the
    // refusal instead of leaving the operator to go and look it up.
    private static void WriteAdvertised(DiscoveredPrinter channel, string indent)
    {
        var advertised = String.IsNullOrWhiteSpace(channel.Info.DriverName)
            ? String.Join(", ", channel.Configuration?.SupportedDocumentFormats ?? [])
            : channel.Info.DriverName;
        if (!String.IsNullOrWhiteSpace(advertised))
        {
            Console.WriteLine($"{indent}This channel reads: {advertised}");
        }
    }

    // The channel is named by its own identifier, so the manager sends to that channel
    // and to no other. The risk is stated above, because a CUPS queue gives no promise.
    private static async Task<JobOutcome> SendRawAsync(
        ServiceProvider provider, Channel target, string directory, string fileName, string contentType)
    {
        var manager = provider.GetRequiredService<IPrinterManager>();
        var what = $"{fileName} raw to {target.Printer.Id}";

        // Thirty seconds to hand the bytes over. There is nothing to watch afterwards, so
        // this budget covers the whole send and no more.
        using CancellationTokenSource submitSource = new(TimeSpan.FromSeconds(30));

        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, fileName), submitSource.Token).ConfigureAwait(false);
            var submitted = await manager.PrintAsync(
                target.Printer.Id,
                PrinterPayload.FromBytes(bytes, contentType),
                new PrintOptions { JobName = fileName },
                submitSource.Token).ConfigureAwait(false);
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
            return new JobOutcome(what, JobResult.Printed, $"job {submitted.JobId} sent");
        }
        catch (NotSupportedException exception)
        {
            // The refusal is the check working: this printer reported it reads other
            // formats only, so the bytes would print as nothing or as source text.
            Console.WriteLine("Nothing was sent, because this printer does not read the format.");
            Console.WriteLine($"Reason: {exception.Message}");
            return new JobOutcome(what, JobResult.Skipped, "the printer does not read the format");
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException)
        {
            Console.WriteLine($"Nothing was sent: {exception.Message}");
            return new JobOutcome(what, JobResult.Failed, exception.Message);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Nothing was sent: the printer did not take the bytes in thirty seconds.");
            return new JobOutcome(what, JobResult.Failed, "the send timed out");
        }
    }

    private static async Task StatusAsync(ServiceProvider provider, IReadOnlyList<PrinterDevice> devices)
    {
        var selected = SampleHelpers.Choose("Printers:", Select(devices));
        if (selected < 0)
        {
            return;
        }

        await ShowStatusAsync(provider.GetRequiredService<IPrinterManager>(), devices[selected]).ConfigureAwait(false);
    }

    private static async Task ShowStatusAsync(IPrinterManager manager, PrinterDevice device)
    {
        Console.WriteLine($"- {device.Details.Name}");
        Console.WriteLine($"  {await ReadStatusAsync(manager, device).ConfigureAwait(false)}");
    }

    // One line about the device as a whole. The manager tries every channel, so a failure
    // here means none of them answered.
    private static async Task<string> ReadStatusAsync(IPrinterManager manager, PrinterDevice device)
    {
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));
        try
        {
            var status = await manager.GetStatusAsync(device.Id, timeoutSource.Token).ConfigureAwait(false);
            StringBuilder line = new(status.State.ToString());
            line.Append(status.IsAcceptingJobs ? ", accepting jobs" : ", not accepting jobs");
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
            return line.ToString();
        }
        catch (Exception exception)
        {
            return $"no status: {exception.Message}";
        }
    }

    // What the printer says it can read is what decides whether a raw send works.
    private static void ShowCapabilities(IReadOnlyList<PrinterDevice> devices)
    {
        var selected = SampleHelpers.Choose("Printers:", Select(devices));
        if (selected >= 0)
        {
            WriteCapabilities(devices[selected]);
        }
    }

    private static void WriteCapabilities(PrinterDevice device) => WriteDevice(device, null);

    // One physical printer, once. What every channel agrees on is said once about the
    // device; only a channel that differs repeats it. Otherwise a printer that advertises
    // IPPS, IPP and the raw port prints the same three lists three times.
    private static void WriteDevice(PrinterDevice device, string status)
    {
        Console.WriteLine();
        Console.WriteLine(status is null ? device.Details.Name : $"{device.Details.Name}  [{status}]");
        Console.WriteLine($"  device   {device.Key.Value}");
        if (device.Details.CommandSets.Count > 0)
        {
            WriteWrapped("cmdsets", device.Details.CommandSets);
        }

        var shared = Shared(device);
        if (shared is not null)
        {
            WriteCapabilityLists(shared);
        }

        Console.WriteLine("  channels");
        foreach (var channel in device.Channels)
        {
            Console.WriteLine($"    {Scheme(channel),-7} {Address(channel),-22} {Summary(channel)}");
            if (shared is null && channel.Configuration is not null)
            {
                WriteCapabilityLists(channel.Configuration, "      ");
            }
        }
    }

    // The capabilities every channel that answered reported, or null when they disagree.
    // A channel that answered nothing is not a disagreement: it did not say.
    private static PrinterConfiguration Shared(PrinterDevice device)
    {
        PrinterConfiguration first = null;
        foreach (var channel in device.Channels)
        {
            if (channel.Configuration is not PrinterConfiguration configuration || configuration.SupportedDocumentFormats.Count == 0)
            {
                continue;
            }

            if (first is null)
            {
                first = configuration;
            }
            else if (!Same(first, configuration))
            {
                return null;
            }
        }

        return first;
    }

    private static bool Same(PrinterConfiguration left, PrinterConfiguration right) =>
        left.SupportedDocumentFormats.SequenceEqual(right.SupportedDocumentFormats)
        && left.SupportedOrientations.SequenceEqual(right.SupportedOrientations)
        && left.SupportedScalings.SequenceEqual(right.SupportedScalings);

    private static void WriteCapabilityLists(PrinterConfiguration configuration, string indent = "  ")
    {
        WriteWrapped("formats", configuration.SupportedDocumentFormats, indent);
        WriteWrapped("rotation", Names(configuration.SupportedOrientations), indent);
        WriteWrapped("scaling", Names(configuration.SupportedScalings), indent);
    }

    // Lower case, so the channel list reads as addresses and not as type names.
    private static string Scheme(DiscoveredPrinter channel) =>
        channel.Endpoint.Scheme.ToString().ToLowerInvariant();

    private static string Address(DiscoveredPrinter channel) => channel.Endpoint switch
    {
        NetworkPrinterEndpoint network => $"{network.Host}:{network.Port}",
        SpoolerPrinterEndpoint spooler => spooler.Name,
        _ => channel.Id.ToString(),
    };

    // What this channel is good for, in a few words. "no answer" is not "no capabilities":
    // the read failed, which on a printer that advertises a port it cannot serve is the
    // normal case.
    private static string Summary(DiscoveredPrinter channel)
    {
        List<string> parts = [];

        // A channel that did not answer must not also claim what it applies: the value is
        // the default of its transport, narrowed by nothing, so it is a guess and not a
        // report. "no answer" is the whole truth about that channel.
        if (channel.Configuration is null && channel.Endpoint.Scheme is not PrinterScheme.Raw)
        {
            parts.Add("no answer");
        }
        else if (channel.SupportedOptions == PrintOptionSupports.All)
        {
            parts.Add("all options");
        }
        else if (channel.SupportedOptions == PrintOptionSupports.None)
        {
            parts.Add("no options");
        }
        else
        {
            parts.Add($"no {PrintOptionSupports.All & ~channel.SupportedOptions}");
        }

        if (channel.GivesPassthrough)
        {
            parts.Add("passthrough");
        }

        if (channel.HasJobQueue)
        {
            parts.Add("job queue");
        }

        return String.Join(", ", parts);
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

    // A long list is wrapped under its own label, so one printer's formats do not push the
    // next printer off the screen.
    private static void WriteWrapped(string label, IReadOnlyList<string> values, string indent = "  ")
    {
        if (values.Count == 0)
        {
            Console.WriteLine($"{indent}{label.PadRight(8)} none reported");
            return;
        }

        var head = indent + label.PadRight(8) + " ";
        var continuation = new string(' ', head.Length);
        var width = Math.Max(40, ConsoleWidth() - head.Length - 1);
        StringBuilder line = new();
        var first = true;
        foreach (var value in values)
        {
            if (line.Length > 0 && line.Length + value.Length + 2 > width)
            {
                // The comma stays on the line it ends, so a wrapped list still reads as one.
                Console.WriteLine($"{(first ? head : continuation)}{line},");
                _ = line.Clear();
                first = false;
            }

            _ = line.Append(line.Length > 0 ? ", " : String.Empty).Append(value);
        }

        Console.WriteLine($"{(first ? head : continuation)}{line}");
    }

    // A redirected output has no width, and the exception it throws must not stop a run.
    private static int ConsoleWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 100;
        }
        catch (IOException)
        {
            return 100;
        }
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
