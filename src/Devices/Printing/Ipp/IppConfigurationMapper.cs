using System.Linq;
using SharpIpp.Protocol.Models;

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
        "document-format-supported",
    ];

    public static PrinterConfiguration Map(PrinterId id, PrinterDescriptionAttributes? attributes)
    {
        if (attributes is null)
        {
            return new PrinterConfiguration(id);
        }

        // An unsent attribute stays null: "not reported" is not "not supported".
        return new PrinterConfiguration(id)
        {
            SupportsColor = attributes.ColorSupported,
            SupportsDuplex = HasDuplex(attributes.SidesSupported),
            MediaSizes = ReadMedia(attributes.MediaSupported),
            DefaultMediaSize = attributes.MediaDefault?.Value,
            SupportedResolutionsDpi = ReadResolutions(attributes.PrinterResolutionSupported),
            SupportedDocumentFormats = ReadFormats(attributes.DocumentFormatSupported),
        };
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
