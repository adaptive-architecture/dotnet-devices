using SharpIpp.Protocol.Models;
using IppColorMode = SharpIpp.Protocol.Models.PrintColorMode;
using IppOrientation = SharpIpp.Protocol.Models.Orientation;
using IppPrintQuality = SharpIpp.Protocol.Models.PrintQuality;
using IppRange = SharpIpp.Protocol.Models.Range;

namespace AdaptArch.Devices.Printing.Ipp;

// An option the caller did not set stays unset, so the printer applies its own default.
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
        template.PrintScaling = IppScalingMapper.Map(options.Scaling);
        template.NumberUp = options.NumberUp;
        template.PrintQuality = MapQuality(options.Quality);
        MapMedia(template, options);

        if (!String.IsNullOrWhiteSpace(options.MediaSource))
        {
            template.MediaSource = new MediaSource(options.MediaSource, true);
        }

        if (!String.IsNullOrWhiteSpace(options.OutputBin))
        {
            template.OutputBin = new OutputBin(options.OutputBin, true);
        }

        if (options.ResolutionDpi is int dpi)
        {
            template.PrinterResolution = new Resolution(dpi, dpi, ResolutionUnit.DotsPerInch, true);
        }

        if (options.PageRanges is { Count: > 0 } ranges)
        {
            List<IppRange> pages = new(ranges.Count);
            foreach (var range in ranges)
            {
                pages.Add(new IppRange(range.Lower, range.Upper));
            }

            template.PageRanges = [.. pages];
        }

        return template;
    }

    // RFC 8011 forbids "media" and "media-col" in one request, and the media type lives
    // only inside "media-col". A job that names a type therefore sends the size there too.
    private static void MapMedia(JobTemplateAttributes template, PrintOptions options)
    {
        var size = String.IsNullOrWhiteSpace(options.MediaSize) ? (Media?)null : new Media(options.MediaSize, true, false);
        if (String.IsNullOrWhiteSpace(options.MediaType))
        {
            template.Media = size;
            return;
        }

        template.MediaCol = new MediaCol
        {
            MediaType = new MediaType(options.MediaType, true),
            MediaSizeName = size,
        };
    }

    private static IppPrintQuality? MapQuality(PrintQuality? quality)
    {
        if (quality == PrintQuality.Draft)
        {
            return IppPrintQuality.Draft;
        }

        if (quality == PrintQuality.Normal)
        {
            return IppPrintQuality.Normal;
        }

        if (quality == PrintQuality.High)
        {
            return IppPrintQuality.High;
        }

        return null;
    }

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

        if (orientation == PrintOrientation.ReverseLandscape)
        {
            return IppOrientation.ReverseLandscape;
        }

        if (orientation == PrintOrientation.ReversePortrait)
        {
            return IppOrientation.ReversePortrait;
        }

        return null;
    }
}
