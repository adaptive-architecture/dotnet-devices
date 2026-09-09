namespace AdaptArch.Devices.Printing.Spooler;

// Interprets the raw buffers DeviceCapabilitiesW fills in. Pure and P/Invoke-free on
// purpose: the buffer layout (fixed 64-character blocks; pairs of horizontal/vertical
// DPI) is documented behaviour of DeviceCapabilitiesW itself, not a struct guess, so
// this is exactly the off-by-one-prone logic worth testing directly with hand-built
// buffers instead of only through a native call nothing here can execute.
internal static class WindowsSpoolerCapabilityParser
{
    private const int PaperNameBlockLength = 64;

    // DC_PAPERNAMES returns one fixed-length block of PaperNameBlockLength characters
    // per name. A name that fills the whole block has no null terminator, so trimming
    // must fall back to the full block length when no null is found.
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

    // DC_ENUMRESOLUTIONS returns pairs of LONG: horizontal DPI followed by vertical
    // DPI. Only the horizontal value is reported, per the task's binding decision.
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
