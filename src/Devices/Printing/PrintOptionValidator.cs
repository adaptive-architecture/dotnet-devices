namespace AdaptArch.Devices.Printing;

// Compares the options against what the printer says it can do. A printer that reported
// nothing cannot judge anything, so the options pass unchanged.
internal static class PrintOptionValidator
{
    public static PrintOptions? Apply(PrintOptions? options, PrinterConfiguration configuration, out IReadOnlyList<string> dropped)
    {
        dropped = [];
        if (options is null || options.OnUnsupported == UnsupportedOptionBehavior.Send || IsEmpty(configuration))
        {
            return options;
        }

        List<string> unsupported = [];
        if (options.Duplex is not null && options.Duplex != DuplexMode.Simplex && !configuration.SupportsDuplex)
        {
            unsupported.Add(nameof(PrintOptions.Duplex));
        }

        if (options.ColorMode == PrintColorMode.Color && !configuration.SupportsColor)
        {
            unsupported.Add(nameof(PrintOptions.ColorMode));
        }

        if (options.MediaSize is not null
            && configuration.MediaSizes.Count > 0
            && !configuration.MediaSizes.Contains(options.MediaSize, StringComparer.Ordinal))
        {
            unsupported.Add(nameof(PrintOptions.MediaSize));
        }

        if (options.ResolutionDpi is int dpi
            && configuration.SupportedResolutionsDpi.Count > 0
            && !configuration.SupportedResolutionsDpi.Contains(dpi))
        {
            unsupported.Add(nameof(PrintOptions.ResolutionDpi));
        }

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

    // A printer that reported no capability at all cannot say what it does not support.
    private static bool IsEmpty(PrinterConfiguration configuration) =>
        !configuration.SupportsDuplex
        && !configuration.SupportsColor
        && configuration.MediaSizes.Count == 0
        && configuration.SupportedResolutionsDpi.Count == 0;

    // The caller keeps its own instance, so the removal happens on a copy.
    private static PrintOptions Without(PrintOptions options, List<string> unsupported)
    {
        var copy = new PrintOptions
        {
            Copies = options.Copies,
            Duplex = options.Duplex,
            ColorMode = options.ColorMode,
            Orientation = options.Orientation,
            MediaSource = options.MediaSource,
            MediaSize = options.MediaSize,
            ResolutionDpi = options.ResolutionDpi,
            JobName = options.JobName,
            OnUnsupported = options.OnUnsupported,
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
        }

        return copy;
    }
}
