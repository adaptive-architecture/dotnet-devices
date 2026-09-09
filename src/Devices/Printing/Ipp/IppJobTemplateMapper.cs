using SharpIpp.Protocol.Models;
using IppColorMode = SharpIpp.Protocol.Models.PrintColorMode;
using IppOrientation = SharpIpp.Protocol.Models.Orientation;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns the library options into IPP job template attributes. An option the caller did not
// set stays unset, so the printer applies its own default.
internal static class IppJobTemplateMapper
{
    public static JobTemplateAttributes Map(PrintOptions? options)
    {
        JobTemplateAttributes template = new();
        if (options is null)
        {
            return template;
        }

        template.Copies = options.Copies;
        template.Sides = MapSides(options.Duplex);
        template.PrintColorMode = MapColor(options.ColorMode);
        template.OrientationRequested = MapOrientation(options.Orientation);
        if (!String.IsNullOrWhiteSpace(options.MediaSize))
        {
            template.Media = new Media(options.MediaSize, true, false);
        }

        if (!String.IsNullOrWhiteSpace(options.MediaSource))
        {
            template.MediaSource = new MediaSource(options.MediaSource, true);
        }

        if (options.ResolutionDpi is int dpi)
        {
            template.PrinterResolution = new Resolution(dpi, dpi, ResolutionUnit.DotsPerInch, true);
        }

        return template;
    }

    // IDE0066 turns off switch expressions in this repository, so each map is a chain
    // of if statements.
    private static Sides? MapSides(DuplexMode? duplex)
    {
        if (duplex == DuplexMode.Simplex)
        {
            return Sides.OneSided;
        }

        if (duplex == DuplexMode.LongEdge)
        {
            return Sides.TwoSidedLongEdge;
        }

        if (duplex == DuplexMode.ShortEdge)
        {
            return Sides.TwoSidedShortEdge;
        }

        return null;
    }

    private static IppColorMode? MapColor(PrintColorMode? mode)
    {
        if (mode == PrintColorMode.Color)
        {
            return IppColorMode.Color;
        }

        if (mode == PrintColorMode.Monochrome)
        {
            return IppColorMode.Monochrome;
        }

        return null;
    }

    private static IppOrientation? MapOrientation(PrintOrientation? orientation)
    {
        if (orientation == PrintOrientation.Portrait)
        {
            return IppOrientation.Portrait;
        }

        if (orientation == PrintOrientation.Landscape)
        {
            return IppOrientation.Landscape;
        }

        return null;
    }
}
