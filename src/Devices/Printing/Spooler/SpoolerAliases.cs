namespace AdaptArch.Devices.Printing.Spooler;

// Turns what a spooler says a queue prints to into the keys that join the queue to its
// device. A queue is not a device: it is one way of reaching one.
internal static class SpoolerAliases
{
    /// <summary>
    /// Reads the aliases a CUPS <c>device-uri</c> carries.
    /// </summary>
    public static IReadOnlyList<PrinterDeviceKey> FromDeviceUri(string? deviceUri)
    {
        if (!DeviceUriParser.TryParse(deviceUri, out var parsed))
        {
            return [];
        }

        List<PrinterDeviceKey> aliases = [];
        if (parsed.Uuid is not null && PrinterDeviceKey.IsUsableIdentity(parsed.Uuid))
        {
            aliases.Add(PrinterDeviceKey.ForDeviceIdentity(parsed.Uuid));
        }

        if (parsed.SerialNumber is not null && PrinterDeviceKey.IsUsableIdentity(parsed.SerialNumber))
        {
            aliases.Add(PrinterDeviceKey.ForDeviceIdentity(parsed.SerialNumber));
        }

        if (parsed.Host is not null)
        {
            aliases.Add(PrinterDeviceKey.ForHost(parsed.Host));
        }

        return aliases;
    }

    /// <summary>
    /// Reads the alias a Windows port name carries.
    /// </summary>
    /// <remarks>
    /// Only the forms that name an address are read. <c>USB001</c> says the queue is on
    /// USB but names no device; a Web Services port, a local port such as <c>LPT1:</c>,
    /// and a connection to another server name no device either. A port can be renamed,
    /// so <c>IP_</c> is a strong hint and never a guarantee: an address is emitted only
    /// when what remains is actually an address.
    /// </remarks>
    public static IReadOnlyList<PrinterDeviceKey> FromPortName(string? portName)
    {
        if (String.IsNullOrWhiteSpace(portName))
        {
            return [];
        }

        // A printer pool lists several ports; the first is the one the queue prints to.
        var first = portName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (first.Length == 0)
        {
            return [];
        }

        var candidate = first[0];

        // A Web Services port is named after a device identifier that is not an address,
        // and it is built only of characters a host name may hold, so it would otherwise
        // pass every test below.
        if (candidate.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (candidate.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[3..];
        }

        // A standard TCP/IP port added a second time for one address is named
        // "<address>_1". The suffix is dropped only when what is left is still an
        // address, so a name that merely ends that way is not truncated.
        var suffix = candidate.LastIndexOf('_');
        if (suffix > 0 && IsAllDigits(candidate.AsSpan()[(suffix + 1)..]) && IsHost(candidate[..suffix]))
        {
            candidate = candidate[..suffix];
        }

        return IsHost(candidate) && !DeviceUriParser.IsLoopbackOrUnspecified(candidate)
            ? [PrinterDeviceKey.ForHost(candidate)]
            : [];
    }

    private static bool IsHost(string value) =>
        !String.IsNullOrWhiteSpace(value) &&
        !value.Contains('\\', StringComparison.Ordinal) &&
        !value.Contains(':', StringComparison.Ordinal) &&
        value.Contains('.', StringComparison.Ordinal) &&
        Uri.CheckHostName(value) != UriHostNameType.Unknown;

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
