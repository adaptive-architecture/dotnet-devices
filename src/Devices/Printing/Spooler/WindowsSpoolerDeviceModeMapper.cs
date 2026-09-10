namespace AdaptArch.Devices.Printing.Spooler;

// Maps PrintOptions onto the device mode fields a Windows print driver reads, and names
// the options that no field can carry. P/Invoke-free on purpose, so the number mapping is
// testable on any platform.
internal static class WindowsSpoolerDeviceModeMapper
{
    // wingdi.h DMORIENT_*.
    private const short OrientationPortrait = 1;
    private const short OrientationLandscape = 2;

    // dmScale is a percentage of the natural size, so "do not scale" is 100 per cent.
    private const short ScaleNone = 100;

    // wingdi.h DMCOLOR_*.
    private const short ColorMonochrome = 1;
    private const short ColorColor = 2;

    // wingdi.h DMDUP_*. Vertical binds the long edge, horizontal the short edge.
    private const short DuplexSimplex = 1;
    private const short DuplexVertical = 2;
    private const short DuplexHorizontal = 3;

    // wingdi.h DMRES_*. A negative dmPrintQuality names a quality, and a positive one is
    // an x resolution in dots per inch. There is no DMRES_ for "normal", so the middle
    // step is DMRES_MEDIUM.
    private const short QualityDraft = -1;
    private const short QualityMedium = -3;
    private const short QualityHigh = -4;

    // Copies is counted as applied because the driver repeats the document one time for
    // each copy: a queue with the RAW data type never reads dmCopies.
    internal static DeviceModeRequest Build(
        PrintOptions? options,
        IReadOnlyList<PrinterMedia> media,
        IReadOnlyList<PrinterMediaSource> sources)
    {
        if (options is null)
        {
            return DeviceModeRequest.Empty;
        }

        var orientation = MapOrientation(options.Orientation);
        var paperSize = MediaNumber(media, options.MediaSize);
        var defaultSource = SourceNumber(sources, options.MediaSource);
        var scale = MapScaling(options.Scaling);
        var color = MapColor(options.ColorMode);
        var duplex = MapDuplex(options.Duplex);
        var resolution = MapResolution(options.ResolutionDpi);

        // dmPrintQuality carries either a resolution or a quality name, so a job that asks
        // for both keeps the resolution: it is the exact number the caller gave.
        var quality = resolution is null ? MapQuality(options.Quality) : null;

        List<string> dropped = [];
        AddIfDropped(dropped, options.Copies is not null, true, nameof(PrintOptions.Copies));
        AddIfDropped(dropped, options.Duplex is not null, duplex is not null, nameof(PrintOptions.Duplex));
        AddIfDropped(dropped, options.ColorMode is not null, color is not null, nameof(PrintOptions.ColorMode));
        AddIfDropped(dropped, options.Orientation is not null, orientation is not null, nameof(PrintOptions.Orientation));
        AddIfDropped(dropped, options.Scaling is not null, scale is not null, nameof(PrintOptions.Scaling));
        AddIfDropped(dropped, options.MediaSource is not null, defaultSource is not null, nameof(PrintOptions.MediaSource));
        AddIfDropped(dropped, options.MediaSize is not null, paperSize is not null, nameof(PrintOptions.MediaSize));
        AddIfDropped(dropped, options.MediaType is not null, false, nameof(PrintOptions.MediaType));
        AddIfDropped(dropped, options.OutputBin is not null, false, nameof(PrintOptions.OutputBin));
        AddIfDropped(dropped, options.ResolutionDpi is not null, resolution is not null, nameof(PrintOptions.ResolutionDpi));
        AddIfDropped(dropped, options.Quality is not null, quality is not null, nameof(PrintOptions.Quality));
        AddIfDropped(dropped, options.PageRanges is not null, false, nameof(PrintOptions.PageRanges));
        AddIfDropped(dropped, options.NumberUp is not null, false, nameof(PrintOptions.NumberUp));

        var printQuality = resolution ?? quality;
        var fields = Bit(orientation, WindowsSpoolerCapabilityParser.DmOrientation)
            | Bit(scale, WindowsSpoolerCapabilityParser.DmScale)
            | Bit(paperSize, WindowsSpoolerCapabilityParser.DmPaperSize)
            | Bit(defaultSource, WindowsSpoolerCapabilityParser.DmDefaultSource)
            | Bit(printQuality, WindowsSpoolerCapabilityParser.DmPrintQuality)
            | Bit(resolution, WindowsSpoolerCapabilityParser.DmYResolution)
            | Bit(color, WindowsSpoolerCapabilityParser.DmColor)
            | Bit(duplex, WindowsSpoolerCapabilityParser.DmDuplex);

        return new DeviceModeRequest(fields, orientation, scale, paperSize, defaultSource, printQuality, resolution, color, duplex, dropped);
    }

    private static void AddIfDropped(List<string> dropped, bool isSet, bool isApplied, string name)
    {
        if (isSet && !isApplied)
        {
            dropped.Add(name);
        }
    }

    private static uint Bit(short? value, uint bit) => value is null ? 0u : bit;

    private static short? MapOrientation(PrintOrientation? orientation)
    {
        if (orientation == PrintOrientation.Portrait)
        {
            return OrientationPortrait;
        }

        if (orientation == PrintOrientation.Landscape)
        {
            return OrientationLandscape;
        }

        return null;
    }

    private static short? MapScaling(PrintScaling? scaling) => scaling == PrintScaling.None ? ScaleNone : null;

    private static short? MapColor(PrintColorMode? colorMode)
    {
        if (colorMode == PrintColorMode.Monochrome)
        {
            return ColorMonochrome;
        }

        if (colorMode == PrintColorMode.Color)
        {
            return ColorColor;
        }

        return null;
    }

    private static short? MapDuplex(DuplexMode? duplex)
    {
        if (duplex == DuplexMode.Simplex)
        {
            return DuplexSimplex;
        }

        if (duplex == DuplexMode.LongEdge)
        {
            return DuplexVertical;
        }

        if (duplex == DuplexMode.ShortEdge)
        {
            return DuplexHorizontal;
        }

        return null;
    }

    private static short? MapQuality(PrintQuality? quality)
    {
        if (quality == PrintQuality.Draft)
        {
            return QualityDraft;
        }

        if (quality == PrintQuality.Normal)
        {
            return QualityMedium;
        }

        if (quality == PrintQuality.High)
        {
            return QualityHigh;
        }

        return null;
    }

    // dmPrintQuality and dmYResolution are signed 16-bit fields, so a resolution that does
    // not fit them cannot be carried at all.
    private static short? MapResolution(int? resolutionDpi) =>
        resolutionDpi is int dpi && dpi is > 0 and <= Int16.MaxValue ? (short)dpi : null;

    // A name the queue did not report has no number, and a number is the only thing a
    // device mode field can hold, so such a name cannot be applied.
    private static short? MediaNumber(IReadOnlyList<PrinterMedia> media, string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (var entry in media)
        {
            if (String.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return AsField(entry.WindowsPaperNumber);
            }
        }

        return null;
    }

    private static short? SourceNumber(IReadOnlyList<PrinterMediaSource> sources, string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (var source in sources)
        {
            if (String.Equals(source.Name, name, StringComparison.Ordinal))
            {
                return AsField(source.WindowsBinNumber);
            }
        }

        return null;
    }

    // DC_PAPERS and DC_BINS answer with WORD values, and the device mode field is signed,
    // so a driver-private number above 0x7FFF comes back as the negative it was written as.
    private static short? AsField(int? number) =>
        number is int value && value is >= 0 and <= UInt16.MaxValue ? (short)(ushort)value : null;
}

// The device mode fields one job asks for. A field is null when the job asked for nothing
// that it can carry, and Dropped names every option that reached no field at all.
internal sealed record DeviceModeRequest(
    uint Fields,
    short? Orientation,
    short? Scale,
    short? PaperSize,
    short? DefaultSource,
    short? PrintQuality,
    short? YResolution,
    short? Color,
    short? Duplex,
    IReadOnlyList<string> Dropped)
{
    internal static readonly DeviceModeRequest Empty = new(0, null, null, null, null, null, null, null, null, []);

    // No field is set, so the job needs no device mode of its own and the queue default stands.
    internal bool IsEmpty => Fields == 0;
}
