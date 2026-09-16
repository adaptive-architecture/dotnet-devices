using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// Turns the library model into the contracts the page reads. The wording is the wording
// the console menu used, so a person who knows the old output recognises this one.
internal static class PrinterProjection
{
    public static IReadOnlyList<DeviceDto> Devices(IReadOnlyList<PrinterDevice> devices)
    {
        List<DeviceDto> projected = [];
        foreach (var device in devices)
        {
            projected.Add(Device(device));
        }

        return projected;
    }

    public static DeviceDto Device(PrinterDevice device)
    {
        var details = device.Details;
        List<ChannelDto> channels = [];
        foreach (var channel in device.Channels)
        {
            channels.Add(Channel(channel));
        }

        return new DeviceDto(
            device.Id.ToString(),
            device.Key.Value,
            details.Name,
            Summary(device),
            details.Manufacturer,
            details.Model,
            details.SerialNumber,
            details.Location,
            Names(details.ContributedBy),
            Names(details.StatusSources),
            details.CommandSets,
            channels);
    }

    // One line for each printer: enough to select it, and to see what it can do.
    private static string Summary(PrinterDevice device)
    {
        var transports = String.Join("/", device.ChannelsByTransport.Keys);
        var traits = device.HasJobQueue ? "job queue" : "no job queue";
        traits += device.GivesPassthrough ? ", passthrough" : ", may convert";
        return $"{transports}; {traits}";
    }

    public static ChannelDto Channel(DiscoveredPrinter channel) =>
        new(
            channel.Id.ToString(),
            channel.Endpoint.Scheme.ToString().ToLowerInvariant(),
            Address(channel),
            Summary(channel),
            channel.HasJobQueue,
            channel.GivesPassthrough,
            PrinterCatalog.Reads(channel),
            Configuration(channel.Configuration));

    private static string Address(DiscoveredPrinter channel) => channel.Endpoint switch
    {
        NetworkPrinterEndpoint network => $"{network.Host}:{network.Port}",
        SpoolerPrinterEndpoint spooler => spooler.Name,
        _ => channel.Id.ToString(),
    };

    // What this channel is good for, in a few words. "no answer" is not "no capabilities":
    // the read failed, which on a printer that advertises a port it cannot serve is the
    // normal case. A channel that did not answer must not also claim what it applies.
    private static string Summary(DiscoveredPrinter channel)
    {
        List<string> parts = [];
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

    // Only what the printer reported reaches the options form. A printer that reported
    // nothing is not asked, because the page would be inventing the choice.
    public static ConfigurationDto Configuration(PrinterConfiguration configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        return new ConfigurationDto(
            configuration.SupportedDocumentFormats,
            Names(configuration.SupportedOrientations),
            Names(configuration.SupportedScalings),
            Media(configuration.Media),
            Sources(configuration.MediaSources),
            configuration.MediaTypes,
            configuration.OutputBins,
            Names(configuration.Qualities),
            configuration.SupportedResolutionsDpi,
            configuration.NumberUpValues,
            configuration.SupportsDuplex,
            configuration.SupportsColor,
            configuration.SupportsPageRanges,
            configuration.DefaultMediaSize,
            configuration.DefaultMediaSource,
            configuration.DefaultOrientation?.ToString(),
            configuration.DefaultResolutionDpi);
    }

    public static StatusDto Status(PrinterStatus status)
    {
        List<string> markers = [];
        foreach (var marker in status.Markers)
        {
            markers.Add($"{marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
        }

        return new StatusDto(
            status.State.ToString(),
            status.IsAcceptingJobs,
            status.Detail,
            status.SerialNumber,
            status.LifetimePageCount,
            markers);
    }

    public static JobDto Job(PrintJobInfo job) =>
        new(job.JobId, job.State.ToString(), job.Detail, job.DroppedOptions);

    // The Windows spooler is the only channel that reports a device mode number, and the
    // number is what a person compares against the printer settings window.
    private static IReadOnlyList<string> Media(IReadOnlyList<PrinterMedia> media)
    {
        List<string> names = [];
        foreach (var value in media)
        {
            names.Add(value.WindowsPaperNumber is int number ? $"{value.Name} ({number})" : value.Name);
        }

        return names;
    }

    private static IReadOnlyList<string> Sources(IReadOnlyList<PrinterMediaSource> sources)
    {
        List<string> names = [];
        foreach (var value in sources)
        {
            names.Add(value.WindowsBinNumber is int number ? $"{value.Name} ({number})" : value.Name);
        }

        return names;
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
}
