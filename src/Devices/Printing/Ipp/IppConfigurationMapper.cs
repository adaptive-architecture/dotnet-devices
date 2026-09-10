using System.Linq;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns the IPP printer attributes into the library model. An absent attribute means the
// printer did not report the capability, which the model shows as an empty list or false.
internal static class IppConfigurationMapper
{
    public static readonly string[] RequestedAttributes =
    [
        "printer-resolution-supported",
        "sides-supported",
        "color-supported",
        "media-supported",
        "media-default",
    ];

    public static PrinterConfiguration Map(PrinterId id, PrinterDescriptionAttributes? attributes)
    {
        if (attributes is null)
        {
            return new PrinterConfiguration(id);
        }

        // An attribute the printer did not send stays null: "not reported" is not "not supported".
        return new PrinterConfiguration(id)
        {
            SupportsColor = attributes.ColorSupported,
            SupportsDuplex = HasDuplex(attributes.SidesSupported),
            MediaSizes = ReadMedia(attributes.MediaSupported),
            DefaultMediaSize = attributes.MediaDefault?.Value,
            SupportedResolutionsDpi = ReadResolutions(attributes.PrinterResolutionSupported),
        };
    }

    // A printer that reports only "one-sided" cannot print on two sides.
    private static bool? HasDuplex(Sides[]? sides) =>
        sides is null ? null : sides.Any(side => side.Value?.StartsWith("two-sided", StringComparison.Ordinal) == true);

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

    // The Printer MIB and IPP both allow dots per centimetre. The model holds dots per
    // inch only, so an entry in another unit is not reported.
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
