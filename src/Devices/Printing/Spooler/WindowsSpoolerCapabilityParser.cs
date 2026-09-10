namespace AdaptArch.Devices.Printing.Spooler;

// Interprets the raw buffers DeviceCapabilitiesW and the device mode hold. P/Invoke-free
// on purpose, so the off-by-one-prone logic is testable with hand-built buffers.
internal static class WindowsSpoolerCapabilityParser
{
    // wingdi.h CCHPAPERNAME and CCHBINNAME.
    internal const int PaperNameBlockLength = 64;
    internal const int BinNameBlockLength = 24;

    // wingdi.h DM_* bits. A device mode field holds a value only when its bit is set, so
    // reading a default and writing a job option both need them. They live here, and not
    // beside the other interop constants, because this class is platform-free and its
    // tests run on Linux. WindowsSpoolerDeviceModeMapper reads them for the write side.
    internal const uint DmOrientation = 0x00000001;
    internal const uint DmPaperSize = 0x00000002;
    internal const uint DmScale = 0x00000010;
    internal const uint DmDefaultSource = 0x00000200;
    internal const uint DmPrintQuality = 0x00000400;
    internal const uint DmColor = 0x00000800;
    internal const uint DmDuplex = 0x00001000;
    internal const uint DmYResolution = 0x00002000;

    // One fixed-length block per name. A name that fills the block has no terminator.
    internal static IReadOnlyList<string> ParseNames(ReadOnlySpan<char> buffer, int count, int blockLength)
    {
        List<string> names = new(count);
        for (var i = 0; i < count; i++)
        {
            var block = buffer.Slice(i * blockLength, blockLength);
            var terminator = block.IndexOf('\0');
            names.Add((terminator < 0 ? block : block[..terminator]).ToString());
        }

        return names;
    }

    internal static IReadOnlyList<string> ParsePaperNames(ReadOnlySpan<char> buffer, int count) =>
        ParseNames(buffer, count, PaperNameBlockLength);

    // The buffer holds WORD values. Marshal.Copy has no ushort overload, so the driver
    // copies into a short array and the sign is undone here.
    internal static IReadOnlyList<int> ParseWords(ReadOnlySpan<short> words)
    {
        List<int> values = new(words.Length);
        foreach (var word in words)
        {
            values.Add((ushort)word);
        }

        return values;
    }

    // Pairs of horizontal and vertical DPI. Only the horizontal value is reported.
    internal static IReadOnlyList<int> ParseResolutions(ReadOnlySpan<int> pairs)
    {
        var count = pairs.Length / 2;
        List<int> resolutions = new(count);
        for (var i = 0; i < count; i++)
        {
            resolutions.Add(pairs[i * 2]);
        }

        return resolutions;
    }

    // The names and the numbers come from two separate DeviceCapabilities calls, so the
    // two lists can disagree. A name with no number keeps a null number.
    internal static IReadOnlyList<PrinterMedia> PairMedia(IReadOnlyList<string> names, IReadOnlyList<int> numbers)
    {
        List<PrinterMedia> media = new(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            media.Add(new PrinterMedia(names[i], i < numbers.Count ? numbers[i] : null));
        }

        return media;
    }

    internal static IReadOnlyList<PrinterMediaSource> PairMediaSources(IReadOnlyList<string> names, IReadOnlyList<int> numbers)
    {
        List<PrinterMediaSource> sources = new(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            sources.Add(new PrinterMediaSource(names[i], i < numbers.Count ? numbers[i] : null));
        }

        return sources;
    }

    // Reads the defaults out of a device mode. A field holds a value only when its
    // DM_ bit is set, and a number is reported as a name, so an unknown number is dropped.
    internal static DeviceModeDefaults ReadDefaults(
        DeviceModeValues values,
        IReadOnlyList<PrinterMedia> media,
        IReadOnlyList<PrinterMediaSource> sources) =>
        new(
            (values.Fields & DmPaperSize) == 0 ? null : MediaName(media, values.PaperSize),
            (values.Fields & DmDefaultSource) == 0 ? null : SourceName(sources, values.DefaultSource),
            (values.Fields & DmOrientation) == 0 ? null : Orientation(values.Orientation),
            ResolutionDpi(values.Fields, values.PrintQuality, values.YResolution));

    private static string? MediaName(IReadOnlyList<PrinterMedia> media, short number)
    {
        foreach (var entry in media)
        {
            if (entry.WindowsPaperNumber == number)
            {
                return entry.Name;
            }
        }

        return null;
    }

    private static string? SourceName(IReadOnlyList<PrinterMediaSource> sources, short number)
    {
        foreach (var source in sources)
        {
            if (source.WindowsBinNumber == number)
            {
                return source.Name;
            }
        }

        return null;
    }

    private static PrintOrientation? Orientation(short orientation)
    {
        if (orientation == 1)
        {
            return PrintOrientation.Portrait;
        }

        if (orientation == 2)
        {
            return PrintOrientation.Landscape;
        }

        return null;
    }

    // A positive dmPrintQuality is an x resolution in dots per inch. A negative one is a
    // DMRES_* quality name, which is not a number of dots, so dmYResolution answers then.
    private static int? ResolutionDpi(uint fields, short printQuality, short yResolution)
    {
        if ((fields & DmPrintQuality) != 0 && printQuality > 0)
        {
            return printQuality;
        }

        if ((fields & DmYResolution) != 0 && yResolution > 0)
        {
            return yResolution;
        }

        return null;
    }
}

// The fields of a device mode this parser reads. Fields announces which of the others
// carries a value.
internal readonly record struct DeviceModeValues(
    uint Fields,
    short Orientation,
    short PaperSize,
    short DefaultSource,
    short PrintQuality,
    short YResolution);

// The defaults a device mode reports. A value is null when the device mode did not
// carry it, or when it named a number that no capability list explains.
internal sealed record DeviceModeDefaults(
    string? MediaSize,
    string? MediaSource,
    PrintOrientation? Orientation,
    int? ResolutionDpi);
