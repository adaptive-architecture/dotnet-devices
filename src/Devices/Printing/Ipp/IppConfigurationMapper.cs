using System.Linq;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;
using IppPrintQuality = SharpIpp.Protocol.Models.PrintQuality;

namespace AdaptArch.Devices.Printing.Ipp;

internal static class IppConfigurationMapper
{
    public static readonly string[] RequestedAttributes =
    [
        "printer-resolution-supported",
        "sides-supported",
        "color-supported",
        "media-supported",
        "media-default",
        "media-source-supported",
        "media-source-default",
        "media-type-supported",
        "output-bin-supported",
        "print-quality-supported",
        "number-up-supported",
        "page-ranges-supported",
        "orientation-requested-default",
        "orientation-requested-supported",
        "print-scaling-supported",
        "printer-resolution-default",
        "document-format-supported",
    ];

    public static PrinterConfiguration Map(PrinterId id, PrinterDescriptionAttributes? attributes, IIppResponseMessage? raw)
    {
        if (attributes is null)
        {
            return new PrinterConfiguration(id);
        }

        var mediaSizes = ReadMedia(attributes.MediaSupported);

        // An unsent attribute stays null: "not reported" is not "not supported".
        return new PrinterConfiguration(id)
        {
            SupportsColor = attributes.ColorSupported,
            SupportsDuplex = HasDuplex(attributes.SidesSupported),
            SupportsPageRanges = attributes.PageRangesSupported,
            MediaSizes = mediaSizes,
            // IPP reports no Windows paper number, so every entry carries the name only.
            Media = mediaSizes.ConvertAll(static name => new PrinterMedia(name, null)),
            MediaSources = ReadMediaSources(attributes.MediaSourceSupported),
            MediaTypes = ReadNames(attributes.MediaTypeSupported?.Select(static type => type.Value)),
            OutputBins = ReadNames(attributes.OutputBinSupported?.Select(static bin => bin.Value)),
            Qualities = ReadQualities(attributes.PrintQualitySupported),
            NumberUpValues = ReadNumberUp(attributes.NumberUpSupported),
            DefaultMediaSize = attributes.MediaDefault?.Value,
            // SharpIppNext does not carry "media-source-default", so it is read raw.
            DefaultMediaSource = IppRawAttributes.ReadText(raw, 0, "media-source-default"),
            DefaultOrientation = MapOrientation(attributes.OrientationRequestedDefault),
            DefaultResolutionDpi = ReadDpi(attributes.PrinterResolutionDefault),
            SupportedResolutionsDpi = ReadResolutions(attributes.PrinterResolutionSupported),
            SupportedDocumentFormats = ReadFormats(attributes.DocumentFormatSupported),
            SupportedOrientations = ReadOrientations(attributes.OrientationRequestedSupported),
            SupportedScalings = ReadScalings(attributes.PrintScalingSupported),
        };
    }

    private static PrintOrientation? MapOrientation(Orientation? orientation)
    {
        if (orientation == Orientation.Portrait)
        {
            return PrintOrientation.Portrait;
        }

        if (orientation == Orientation.Landscape)
        {
            return PrintOrientation.Landscape;
        }

        if (orientation == Orientation.ReverseLandscape)
        {
            return PrintOrientation.ReverseLandscape;
        }

        if (orientation == Orientation.ReversePortrait)
        {
            return PrintOrientation.ReversePortrait;
        }

        return null;
    }

    private static List<PrintOrientation> ReadOrientations(Orientation[]? orientations)
    {
        if (orientations is null || orientations.Length == 0)
        {
            return [];
        }

        List<PrintOrientation> result = new(orientations.Length);
        foreach (var orientation in orientations)
        {
            // A value this library does not model is left out, not guessed.
            if (MapOrientation(orientation) is PrintOrientation mapped && !result.Contains(mapped))
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    // "print-scaling" is a keyword, so the model carries it as a string-like value.
    private static List<PrintScaling> ReadScalings(SharpIpp.Protocol.Models.PrintScaling[]? scalings)
    {
        if (scalings is null || scalings.Length == 0)
        {
            return [];
        }

        List<PrintScaling> result = new(scalings.Length);
        foreach (var scaling in scalings)
        {
            if (IppScalingMapper.Map(scaling) is PrintScaling mapped && !result.Contains(mapped))
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    // The model holds dots per inch only, so a value in another unit is not reported.
    private static int? ReadDpi(Resolution? resolution)
    {
        if (resolution is Resolution value && value.Units == ResolutionUnit.DotsPerInch)
        {
            return value.Width;
        }

        return null;
    }

    private static List<string> ReadNames(IEnumerable<string?>? names)
    {
        if (names is null)
        {
            return [];
        }

        return [.. names.OfType<string>().Where(static name => !String.IsNullOrWhiteSpace(name))];
    }

    private static List<PrinterMediaSource> ReadMediaSources(MediaSource[]? sources)
    {
        if (sources is null || sources.Length == 0)
        {
            return [];
        }

        // IPP names a tray; only the Windows driver knows a bin number for it.
        return [.. sources
            .Where(static source => !String.IsNullOrWhiteSpace(source.Value))
            .Select(static source => new PrinterMediaSource(source.Value, null))];
    }

    private static List<PrintQuality> ReadQualities(IppPrintQuality[]? qualities)
    {
        if (qualities is null || qualities.Length == 0)
        {
            return [];
        }

        List<PrintQuality> result = new(qualities.Length);
        foreach (var quality in qualities)
        {
            var mapped = (PrintQuality)(int)quality;
            if (Enum.IsDefined(mapped) && !result.Contains(mapped))
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    // "number-up-supported" is a set of integers, of ranges, or of both. A range is
    // expanded, because the model holds the values a caller may ask for.
    private static List<int> ReadNumberUp(SharpIpp.Protocol.Models.Range[]? ranges)
    {
        if (ranges is null || ranges.Length == 0)
        {
            return [];
        }

        List<int> values = [];
        foreach (var range in ranges)
        {
            for (var value = range.Lower; value <= range.Upper; value++)
            {
                if (value > 0 && !values.Contains(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    // A printer that reports only "one-sided" cannot print on two sides.
    private static bool? HasDuplex(Sides[]? sides) =>
        sides is null ? null : sides.Any(side => side.Value?.StartsWith("two-sided", StringComparison.Ordinal) == true);

    private static List<string> ReadFormats(string[]? formats)
    {
        if (formats is null || formats.Length == 0)
        {
            return [];
        }

        return formats
            .Where(format => !String.IsNullOrWhiteSpace(format))
            .ToList();
    }

    private static List<string> ReadMedia(Media[]? media)
    {
        if (media is null || media.Length == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (var entry in media.Where(entry => !String.IsNullOrWhiteSpace(entry.Value)))
        {
            names.Add(entry.Value);
        }

        return names;
    }

    // The model holds dots per inch only, so an entry in another unit is dropped.
    private static List<int> ReadResolutions(Resolution[]? resolutions)
    {
        if (resolutions is null || resolutions.Length == 0)
        {
            return [];
        }

        List<int> dpi = [];
        foreach (var resolution in resolutions)
        {
            if (resolution.Units == ResolutionUnit.DotsPerInch && !dpi.Contains(resolution.Width))
            {
                dpi.Add(resolution.Width);
            }
        }

        return dpi;
    }
}
