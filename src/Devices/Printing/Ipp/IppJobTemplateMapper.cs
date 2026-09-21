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
    // only inside "media-col". A job that names a type therefore sends the size there too,
    // and so does one that gives a size the printer has no name for: PWG 5100.7 puts those
    // dimensions in "media-size", which lives only there as well.
    private static void MapMedia(JobTemplateAttributes template, PrintOptions options)
    {
        var size = String.IsNullOrWhiteSpace(options.MediaSize) ? (Media?)null : new Media(options.MediaSize, true, false);

        // A name the printer knows describes stock it has loaded; a pair of numbers does not,
        // so the name wins and the dimensions are left out.
        var dimensions = size is null ? options.MediaDimensions : null;
        if (String.IsNullOrWhiteSpace(options.MediaType) && dimensions is null)
        {
            template.Media = size;
            return;
        }

        MediaCol col = new()
        {
            MediaSizeName = size,
        };

        if (!String.IsNullOrWhiteSpace(options.MediaType))
        {
            col.MediaType = new MediaType(options.MediaType, true);
        }

        if (dimensions is not null)
        {
            // media-size counts in hundredths of a millimetre, which is what PrintLength holds.
            col.MediaSize = new MediaSize
            {
                XDimension = dimensions.Width.HundredthsOfMillimeter,
                YDimension = dimensions.Height.HundredthsOfMillimeter,
            };
        }

        template.MediaCol = col;
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
