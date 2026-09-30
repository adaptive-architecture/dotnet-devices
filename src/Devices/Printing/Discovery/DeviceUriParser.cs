using System.Net;

namespace AdaptArch.Devices.Printing;

// Reads what a spooler says its queue points at. CUPS reports it as "device-uri":
//
//     ipp://192.168.1.5:631/ipp/print      the queue prints to that host
//     socket://192.168.1.5:9100            the same, over the raw channel
//     usb://Zebra/ZTC%20ZD421?serial=X4TY  the queue prints to that USB device
//     dnssd://Printer._ipp._tcp.local/?uuid=e3248000-...
//
// This is the only evidence that ties a print queue to the device behind it, so it is
// what merges a spooler channel with the network channels of one printer.
internal sealed class DeviceUriParser
{
    private const string UsbPrefix = "usb://";

    // The schemes whose authority is genuinely a host. Everything else is read only for
    // an identity in its query, because its authority names something else: CUPS writes
    // "implicitclass://<queue>/" for a driverless queue and "dnssd://<instance>/" for a
    // discovered one, and reading either as an address would invent a host that is only
    // the name of the queue written differently.
    private static readonly string[] HostSchemes = ["ipp", "ipps", "http", "https", "socket", "lpd"];

    // The backends that pass a job on to another IPP printer, which then decides what the
    // bytes are. "dnssd" resolves to whichever service its instance names, so only the IPP
    // services count; cups-browsed's "implicitclass" always forwards over IPP.
    private static readonly string[] IppForwardingSchemes = ["ipp", "ipps", "http", "https", "implicitclass"];

    private DeviceUriParser()
    {
    }

    /// <summary>
    /// Tells whether a CUPS queue with this device URI forwards its jobs over IPP.
    /// </summary>
    /// <returns><c>null</c> when there is no device URI or it cannot be read.</returns>
    public static bool? ForwardsOverIpp(string? value)
    {
        // System.Uri refuses the underscores of a dnssd instance, so the scheme is read by hand.
        var colon = value?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (colon <= 0)
        {
            return null;
        }

        var scheme = value![..colon];
        if (String.Equals(scheme, "dnssd", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("._ipp._tcp", StringComparison.OrdinalIgnoreCase)
                || value.Contains("._ipps._tcp", StringComparison.OrdinalIgnoreCase);
        }

        return Array.Exists(IppForwardingSchemes, known => String.Equals(scheme, known, StringComparison.OrdinalIgnoreCase));
    }

    public string? Host { get; private set; }

    public int? Port { get; private set; }

    public string? SerialNumber { get; private set; }

    public string? Uuid { get; private set; }

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    /// <summary>
    /// Gets a value indicating whether anything usable was read.
    /// </summary>
    public bool HasEvidence => Host is not null || SerialNumber is not null || Uuid is not null;

    /// <summary>
    /// Reads a device URI.
    /// </summary>
    /// <returns><c>false</c> when the URI names no device, for example <c>file:/dev/usb/lp0</c>.</returns>
    public static bool TryParse(string? value, out DeviceUriParser parsed)
    {
        parsed = new DeviceUriParser();
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // System.Uri lowercases an authority, which would destroy the manufacturer name
        // that a USB device URI carries there, so that one shape is read by hand.
        if (value.StartsWith(UsbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            parsed.ReadUsb(value[UsbPrefix.Length..]);
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && value.Contains("//", StringComparison.Ordinal))
        {
            parsed.ReadAuthority(uri);
            parsed.ReadQuery(uri.Query);
        }
        else
        {
            // A scheme with no authority, such as "hp:/usb/HP_LaserJet?serial=X".
            var mark = value.IndexOf('?', StringComparison.Ordinal);
            if (mark >= 0)
            {
                parsed.ReadQuery(value[mark..]);
            }
        }

        return parsed.HasEvidence;
    }

    // usb://<manufacturer>/<model>?serial=<serial>
    private void ReadUsb(string rest)
    {
        var mark = rest.IndexOf('?', StringComparison.Ordinal);
        var path = mark < 0 ? rest : rest[..mark];
        if (mark >= 0)
        {
            ReadQuery(rest[mark..]);
        }

        var separator = path.IndexOf('/', StringComparison.Ordinal);
        var manufacturer = separator < 0 ? path : path[..separator];
        var model = separator < 0 ? String.Empty : path[(separator + 1)..].Trim('/');
        Manufacturer = manufacturer.Length > 0 ? Decode(manufacturer) : null;
        Model = model.Length > 0 ? Decode(model) : null;
    }

    private void ReadAuthority(Uri uri)
    {
        if (!IsHostScheme(uri.Scheme))
        {
            return;
        }

        // A name that resolves nowhere useful must not become an identity: a queue that
        // prints to the local daemon would otherwise merge with every other one.
        if (String.IsNullOrEmpty(uri.Host) ||
            IsLoopbackOrUnspecified(uri.Host) ||
            Uri.CheckHostName(uri.Host) == UriHostNameType.Unknown)
        {
            return;
        }

        Host = uri.Host;
        if (!uri.IsDefaultPort && uri.Port is > 0 and <= 65535)
        {
            Port = uri.Port;
        }
    }

    private void ReadQuery(string query)
    {
        if (String.IsNullOrEmpty(query))
        {
            return;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = pair[..separator];
            var text = Decode(pair[(separator + 1)..]);
            if (String.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (SerialNumber is null && String.Equals(key, "serial", StringComparison.OrdinalIgnoreCase))
            {
                SerialNumber = text;
            }
            else if (Uuid is null && String.Equals(key, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                Uuid = NormalizeUuid(text);
            }
        }
    }

    /// <summary>
    /// Strips the <c>urn:uuid:</c> prefix and the braces that some printers add, so the
    /// value matches the one multicast DNS reports for the same device.
    /// </summary>
    public static string? NormalizeUuid(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["urn:uuid:".Length..];
        }

        text = text.Trim('{', '}');
        return Guid.TryParse(text, out var uuid) ? uuid.ToString("D") : null;
    }

    private static bool IsHostScheme(string scheme) =>
        Array.Exists(HostSchemes, known => String.Equals(scheme, known, StringComparison.OrdinalIgnoreCase));

    internal static bool IsLoopbackOrUnspecified(string host)
    {
        if (String.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        return IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any);
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value);
}
