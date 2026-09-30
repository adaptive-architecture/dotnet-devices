namespace AdaptArch.Devices.Printing;

// A printer that reported nothing cannot judge anything, so the options pass unchanged.
internal static class PrintOptionValidator
{
    private const string CapabilityReason = "the printer does not list the value among its capabilities";

    public static PrintOptions? Apply(PrintOptions? options, PrinterConfiguration configuration, out IReadOnlyList<DroppedOption> dropped)
    {
        dropped = [];
        if (options is null || options.OnUnsupported == UnsupportedOptionBehavior.Send || IsEmpty(configuration))
        {
            return options;
        }

        var unsupported = Collect(options, configuration);
        if (unsupported.Count == 0)
        {
            return options;
        }

        if (options.OnUnsupported == UnsupportedOptionBehavior.Throw)
        {
            throw new NotSupportedException(
                $"Printer '{configuration.PrinterId}' does not support {String.Join(", ", unsupported)}.");
        }

        dropped = Dropped(unsupported, PrintOptionStage.PrinterCapabilities, CapabilityReason);
        return Without(options, unsupported);
    }

    // Every option the printer said it does not apply. A capability the printer did not
    // report judges nothing, so an empty list on the configuration lets the option pass.
    private static List<string> Collect(PrintOptions options, PrinterConfiguration configuration)
    {
        List<string> unsupported = [];
        CollectSheet(options, configuration, unsupported);
        CollectMedia(options, configuration, unsupported);
        CollectQuality(options, configuration, unsupported);
        CollectLayout(options, configuration, unsupported);
        return unsupported;
    }

    // What the printer does with the sheet itself.
    private static void CollectSheet(PrintOptions options, PrinterConfiguration configuration, List<string> unsupported)
    {
        if (options.Duplex is not null && options.Duplex != DuplexMode.Simplex && configuration.SupportsDuplex == false)
        {
            unsupported.Add(nameof(PrintOptions.Duplex));
        }

        if (options.ColorMode == PrintColorMode.Color && configuration.SupportsColor == false)
        {
            unsupported.Add(nameof(PrintOptions.ColorMode));
        }

        if (options.PageRanges is { Count: > 0 } && configuration.SupportsPageRanges == false)
        {
            unsupported.Add(nameof(PrintOptions.PageRanges));
        }
    }

    // The paper, where it comes from and where it goes.
    private static void CollectMedia(PrintOptions options, PrinterConfiguration configuration, List<string> unsupported)
    {
        if (options.MediaSize is not null
            && configuration.MediaSizes.Count > 0
            && !configuration.MediaSizes.Contains(options.MediaSize, StringComparer.Ordinal))
        {
            unsupported.Add(nameof(PrintOptions.MediaSize));
        }

        if (options.MediaSource is string mediaSource
            && configuration.MediaSources.Count > 0
            && !HasName(configuration.MediaSources, mediaSource))
        {
            unsupported.Add(nameof(PrintOptions.MediaSource));
        }

        if (options.MediaType is not null
            && configuration.MediaTypes.Count > 0
            && !configuration.MediaTypes.Contains(options.MediaType, StringComparer.Ordinal))
        {
            unsupported.Add(nameof(PrintOptions.MediaType));
        }

        if (options.OutputBin is not null
            && configuration.OutputBins.Count > 0
            && !configuration.OutputBins.Contains(options.OutputBin, StringComparer.Ordinal))
        {
            unsupported.Add(nameof(PrintOptions.OutputBin));
        }
    }

    // How well the printer puts the ink down.
    private static void CollectQuality(PrintOptions options, PrinterConfiguration configuration, List<string> unsupported)
    {
        if (options.ResolutionDpi is int dpi
            && configuration.SupportedResolutionsDpi.Count > 0
            && !configuration.SupportedResolutionsDpi.Contains(dpi))
        {
            unsupported.Add(nameof(PrintOptions.ResolutionDpi));
        }

        if (options.Quality is PrintQuality quality
            && configuration.Qualities.Count > 0
            && !configuration.Qualities.Contains(quality))
        {
            unsupported.Add(nameof(PrintOptions.Quality));
        }
    }

    // How the pages are arranged on the sheet.
    private static void CollectLayout(PrintOptions options, PrinterConfiguration configuration, List<string> unsupported)
    {
        if (options.NumberUp is int pages
            && configuration.NumberUpValues.Count > 0
            && !configuration.NumberUpValues.Contains(pages))
        {
            unsupported.Add(nameof(PrintOptions.NumberUp));
        }

        if (options.Orientation is PrintOrientation orientation
            && configuration.SupportedOrientations.Count > 0
            && !configuration.SupportedOrientations.Contains(orientation))
        {
            unsupported.Add(nameof(PrintOptions.Orientation));
        }

        if (options.Scaling is PrintScaling scaling
            && configuration.SupportedScalings.Count > 0
            && !configuration.SupportedScalings.Contains(scaling))
        {
            unsupported.Add(nameof(PrintOptions.Scaling));
        }
    }

    public static DroppedOption[] Dropped(IEnumerable<string> names, PrintOptionStage stage, string reason) =>
        names.Select(name => new DroppedOption(name, stage, reason)).ToArray();

    // Every option set away from its default that asks the device for something. The job
    // name, the user name, the password and OnUnsupported steer the library, not the sheet.
    public static List<string> SetOptions(PrintOptions options)
    {
        List<string> set = [];
        AddIfSet(set, options.Copies is not null, nameof(PrintOptions.Copies));
        AddIfSet(set, options.Duplex is not null, nameof(PrintOptions.Duplex));
        AddIfSet(set, options.ColorMode is not null, nameof(PrintOptions.ColorMode));
        AddIfSet(set, options.Orientation is not null, nameof(PrintOptions.Orientation));
        AddIfSet(set, options.Scaling is not null, nameof(PrintOptions.Scaling));
        AddIfSet(set, options.FitArea != PrintFitArea.Printable, nameof(PrintOptions.FitArea));
        AddIfSet(set, options.MediaSource is not null, nameof(PrintOptions.MediaSource));
        AddIfSet(set, options.MediaSize is not null, nameof(PrintOptions.MediaSize));
        AddIfSet(set, options.MediaDimensions is not null, nameof(PrintOptions.MediaDimensions));
        AddIfSet(set, options.MediaSizeSource != MediaSizeSource.Printer, nameof(PrintOptions.MediaSizeSource));
        AddIfSet(set, options.Placement is { IsEmpty: false }, nameof(PrintOptions.Placement));
        AddIfSet(set, options.Smoothing is not null, nameof(PrintOptions.Smoothing));
        AddIfSet(set, options.MediaType is not null, nameof(PrintOptions.MediaType));
        AddIfSet(set, options.OutputBin is not null, nameof(PrintOptions.OutputBin));
        AddIfSet(set, options.ResolutionDpi is not null, nameof(PrintOptions.ResolutionDpi));
        AddIfSet(set, options.Quality is not null, nameof(PrintOptions.Quality));
        AddIfSet(set, options.PageRanges is not null, nameof(PrintOptions.PageRanges));
        AddIfSet(set, options.ConverterName is not null, nameof(PrintOptions.ConverterName));
        AddIfSet(set, options.NumberUp is not null, nameof(PrintOptions.NumberUp));
        return set;
    }

    // The named options this job set, reported as dropped at one stage for one reason.
    public static DroppedOption[] Unapplied(PrintOptions? options, PrintOptionStage stage, string reason, params string[] names) =>
        options is null ? [] : Dropped(SetOptions(options).Where(names.Contains), stage, reason);

    // The drops found above a driver come first, and the driver never repeats one of them.
    // A collection expression over two lists emits a compiler wrapper type that the trimmer
    // cannot keep intact, with no analyzer warning. A plain list is safe.
    public static void Prepend(PrintJobInfo job, IReadOnlyList<DroppedOption> dropped)
    {
        List<DroppedOption> all = new(dropped.Count + job.DroppedOptionDetails.Count);
        all.AddRange(dropped);
        all.AddRange(job.DroppedOptionDetails);
        job.DroppedOptionDetails = all;
    }

    private static void AddIfSet(List<string> set, bool isSet, string name)
    {
        if (isSet)
        {
            set.Add(name);
        }
    }

    private static bool HasName(IReadOnlyList<PrinterMediaSource> sources, string name) =>
        sources.Any(source => String.Equals(source.Name, name, StringComparison.Ordinal));

    // A reported "false" is a capability statement, so it is not an empty configuration.
    private static bool IsEmpty(PrinterConfiguration configuration) =>
        configuration.SupportsDuplex is null
        && configuration.SupportsColor is null
        && configuration.SupportsPageRanges is null
        && configuration.MediaSizes.Count == 0
        && configuration.MediaSources.Count == 0
        && configuration.MediaTypes.Count == 0
        && configuration.OutputBins.Count == 0
        && configuration.Qualities.Count == 0
        && configuration.NumberUpValues.Count == 0
        && configuration.SupportedResolutionsDpi.Count == 0
        && configuration.SupportedOrientations.Count == 0
        && configuration.SupportedScalings.Count == 0;

    // A converter that honoured the page ranges leaves none for the printer to apply a
    // second time, which would select a subset of the subset.
    public static PrintOptions WithoutPageRanges(PrintOptions options)
    {
        var copy = Copy(options);
        copy.PageRanges = null;
        return copy;
    }

    // A converter that composed the page onto the media put the fit into the pixels. Asking
    // the printer for it as well fits the page twice, and a printer that fits to its own
    // printable area then shrinks a media-sized raster and takes every placed offset with it.
    // "None" and not "unset": a printer with nothing to go on defaults to "auto", which
    // shrinks just the same.
    public static PrintOptions WithoutPlacedGeometry(PrintOptions options)
    {
        var copy = Copy(options);
        copy.PageRanges = null;
        copy.Scaling = PrintScaling.None;
        return copy;
    }

    private static PrintOptions Without(PrintOptions options, List<string> unsupported)
    {
        var copy = Copy(options);
        foreach (var name in unsupported)
        {
            ApplyRemoval(copy, name);
        }

        return copy;
    }

    private static PrintOptions Copy(PrintOptions options) =>
        new()
        {
            Copies = options.Copies,
            Duplex = options.Duplex,
            ColorMode = options.ColorMode,
            Orientation = options.Orientation,
            Scaling = options.Scaling,
            MediaSource = options.MediaSource,
            MediaSize = options.MediaSize,
            MediaDimensions = options.MediaDimensions,
            MediaSizeSource = options.MediaSizeSource,
            FitArea = options.FitArea,
            DocumentPassword = options.DocumentPassword,
            Placement = options.Placement,
            Smoothing = options.Smoothing,
            MediaType = options.MediaType,
            OutputBin = options.OutputBin,
            ResolutionDpi = options.ResolutionDpi,
            Quality = options.Quality,
            PageRanges = options.PageRanges,
            NumberUp = options.NumberUp,
            JobName = options.JobName,
            RequestingUserName = options.RequestingUserName,
            // A copy that lost the engine the job named would quietly render with another
            // one, which is the behaviour naming an engine exists to prevent.
            ConverterName = options.ConverterName,
            OnUnsupported = options.OnUnsupported,
        };

    private static void ApplyRemoval(PrintOptions copy, string name)
    {
        if (name == nameof(PrintOptions.Duplex))
        {
            copy.Duplex = null;
        }
        else if (name == nameof(PrintOptions.ColorMode))
        {
            copy.ColorMode = null;
        }
        else if (name == nameof(PrintOptions.MediaSize))
        {
            copy.MediaSize = null;
        }
        else if (name == nameof(PrintOptions.ResolutionDpi))
        {
            copy.ResolutionDpi = null;
        }
        else if (name == nameof(PrintOptions.MediaSource))
        {
            copy.MediaSource = null;
        }
        else if (name == nameof(PrintOptions.MediaType))
        {
            copy.MediaType = null;
        }
        else if (name == nameof(PrintOptions.OutputBin))
        {
            copy.OutputBin = null;
        }
        else if (name == nameof(PrintOptions.Quality))
        {
            copy.Quality = null;
        }
        else if (name == nameof(PrintOptions.NumberUp))
        {
            copy.NumberUp = null;
        }
        else if (name == nameof(PrintOptions.PageRanges))
        {
            copy.PageRanges = null;
        }
        else if (name == nameof(PrintOptions.Orientation))
        {
            copy.Orientation = null;
        }
        else if (name == nameof(PrintOptions.Scaling))
        {
            copy.Scaling = null;
        }
    }
}
