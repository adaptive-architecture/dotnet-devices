namespace AdaptArch.Devices.Printing;

// A printer that reported nothing cannot judge anything, so the options pass unchanged.
internal static class PrintOptionValidator
{
    public static PrintOptions? Apply(PrintOptions? options, PrinterConfiguration configuration, out IReadOnlyList<string> dropped)
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

        dropped = unsupported;
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

    private static PrintOptions Without(PrintOptions options, List<string> unsupported)
    {
        var copy = new PrintOptions
        {
            Copies = options.Copies,
            Duplex = options.Duplex,
            ColorMode = options.ColorMode,
            Orientation = options.Orientation,
            Scaling = options.Scaling,
            MediaSource = options.MediaSource,
            MediaSize = options.MediaSize,
            MediaType = options.MediaType,
            OutputBin = options.OutputBin,
            ResolutionDpi = options.ResolutionDpi,
            Quality = options.Quality,
            PageRanges = options.PageRanges,
            NumberUp = options.NumberUp,
            JobName = options.JobName,
            RequestingUserName = options.RequestingUserName,
            OnUnsupported = options.OnUnsupported,
            RequirePassthrough = options.RequirePassthrough,
        };

        foreach (var name in unsupported)
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

        return copy;
    }
}
