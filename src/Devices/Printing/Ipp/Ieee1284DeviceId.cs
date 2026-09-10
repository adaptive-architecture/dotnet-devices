namespace AdaptArch.Devices.Printing.Ipp;

// Reads an IEEE 1284 device identification string, which IPP carries as
// "printer-device-id" and multicast DNS carries in its "usb_" TXT keys.
//
// The serial number in it is the only device identity that IPP reports besides the UUID,
// and many printers report one and not the other.
internal sealed class Ieee1284DeviceId
{
    private Ieee1284DeviceId()
    {
    }

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    public string? SerialNumber { get; private set; }

    public IReadOnlyList<string> CommandSets { get; private set; } = [];

    /// <summary>
    /// Reads the string. A key that appears more than one time keeps its first value.
    /// </summary>
    /// <returns><c>false</c> when the value holds no pair at all.</returns>
    public static bool TryParse(string? value, out Ieee1284DeviceId deviceId)
    {
        deviceId = new Ieee1284DeviceId();
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var found = false;
        List<string> commandSets = [];
        foreach (var range in value.AsSpan().Split(';'))
        {
            var pair = value.AsSpan()[range].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            // Only the first colon separates: a value legitimately holds more, as in
            // "CMD:ZPL:2,XML".
            var separator = pair.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = pair[..separator].Trim();
            var text = pair[(separator + 1)..].Trim();
            if (text.IsEmpty)
            {
                continue;
            }

            found = true;
            deviceId.Assign(key, text.ToString(), commandSets);
        }

        deviceId.CommandSets = commandSets;
        return found;
    }

    private void Assign(ReadOnlySpan<char> key, string text, List<string> commandSets)
    {
        if (Manufacturer is null && (Matches(key, "MFG") || Matches(key, "MANUFACTURER")))
        {
            Manufacturer = text;
            return;
        }

        if (Model is null && (Matches(key, "MDL") || Matches(key, "MODEL")))
        {
            Model = text;
            return;
        }

        if (SerialNumber is null && (Matches(key, "SN") || Matches(key, "SERN") || Matches(key, "SERIALNUMBER")))
        {
            SerialNumber = text;
            return;
        }

        if (commandSets.Count == 0 && (Matches(key, "CMD") || Matches(key, "COMMAND SET") || Matches(key, "COMMANDSET")))
        {
            commandSets.AddRange(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    private static bool Matches(ReadOnlySpan<char> key, string name) =>
        key.Equals(name, StringComparison.OrdinalIgnoreCase);
}
