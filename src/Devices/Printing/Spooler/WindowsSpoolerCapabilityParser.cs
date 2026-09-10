namespace AdaptArch.Devices.Printing.Spooler;

// Interprets the raw buffers DeviceCapabilitiesW fills in. P/Invoke-free on purpose, so
// the off-by-one-prone logic is testable with hand-built buffers.
internal static class WindowsSpoolerCapabilityParser
{
    private const int PaperNameBlockLength = 64;

    // One fixed-length block per name. A name that fills the block has no terminator.
    internal static IReadOnlyList<string> ParsePaperNames(ReadOnlySpan<char> buffer, int count)
    {
        List<string> names = new(count);
        for (var i = 0; i < count; i++)
        {
            var block = buffer.Slice(i * PaperNameBlockLength, PaperNameBlockLength);
            var terminator = block.IndexOf('\0');
            names.Add((terminator < 0 ? block : block[..terminator]).ToString());
        }

        return names;
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
}
