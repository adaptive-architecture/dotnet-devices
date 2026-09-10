using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Samples;

internal static class SampleHelpers
{
    private const int MaxProbeHosts = 4096;

    // Printing costs paper and ink, so the sample always asks first.
    internal static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N]: ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    // Prints a numbered list and reads one number. Returns -1 for a cancel or a bad entry.
    internal static int Choose(string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            Console.WriteLine($"{title}: nothing to choose from.");
            return -1;
        }

        Console.WriteLine(title);
        for (var index = 0; index < items.Count; index++)
        {
            Console.WriteLine($"  {index + 1}) {items[index]}");
        }

        Console.Write("Number (empty to cancel): ");
        var answer = Console.ReadLine();
        if (!Int32.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var choice)
            || choice < 1 || choice > items.Count)
        {
            Console.WriteLine("Cancelled.");
            return -1;
        }

        return choice - 1;
    }

    // Three states: true the channel reports the format, false it reports other formats
    // only, null it reports nothing. A channel that reports nothing did not refuse.
    //
    // The judgement is per channel, never per device. The channels of one printer read
    // different formats: an IPP service can take a JPEG that the raw port, which feeds the
    // bytes straight to the page description language interpreter, cannot.
    internal static bool? Accepts(PrinterDevice device, DiscoveredPrinter channel, string contentType)
    {
        // The "pdl" record of the advertisement names what this channel itself reads, so
        // it answers before anything the device says about itself as a whole.
        var advertised = SplitFormats(channel.Info.DriverName);
        if (advertised.Count > 0)
        {
            return advertised.Contains(contentType, StringComparer.OrdinalIgnoreCase);
        }

        var formats = channel.Configuration?.SupportedDocumentFormats ?? [];
        if (formats.Count > 0)
        {
            return formats.Contains(contentType, StringComparer.OrdinalIgnoreCase);
        }

        // IEEE 1284 names the languages the firmware reads. It belongs to the device, not
        // to one channel, so it is the last word and not the first.
        if (device.Details.CommandSets.Count == 0)
        {
            return null;
        }

        var commandSet = GetCommandSet(contentType);
        foreach (var command in device.Details.CommandSets)
        {
            if (command.Contains(commandSet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // The "pdl" TXT record is a comma-separated list of media types.
    private static List<string> SplitFormats(string list)
    {
        List<string> formats = [];
        if (String.IsNullOrWhiteSpace(list))
        {
            return formats;
        }

        foreach (var entry in list.Split(','))
        {
            var format = entry.Trim();
            if (format.Length > 0)
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    private static string GetCommandSet(string contentType)
    {
        if (contentType == PrinterContentTypes.Zpl)
        {
            return "ZPL";
        }

        if (contentType == PrinterContentTypes.Epl)
        {
            return "EPL";
        }

        if (contentType == PrinterContentTypes.Pdf)
        {
            return "PDF";
        }

        if (contentType == PrinterContentTypes.Png)
        {
            return "PNG";
        }

        return contentType;
    }

    // An identifier is a URI. A bare address is still accepted, and read as the raw
    // channel of that host, because typing one is convenient.
    internal static PrinterId ParsePrinterId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return PrinterId.TryParse(value, out var id) ? id : PrinterId.ForRaw(value);
    }

    internal static void AppendMarkers(StringBuilder line, IReadOnlyList<PrinterMarker> markers)
    {
        foreach (var marker in markers)
        {
            line.Append($"; {marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
        }
    }

    internal static IReadOnlyList<string> GetPrintFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<string> files = [];
        foreach (var path in Directory.GetFiles(directory))
        {
            files.Add(Path.GetFileName(path));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    internal static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".zpl")
        {
            return PrinterContentTypes.Zpl;
        }

        if (extension == ".epl")
        {
            return PrinterContentTypes.Epl;
        }

        if (extension == ".png")
        {
            return PrinterContentTypes.Png;
        }

        if (extension is ".jpg" or ".jpeg")
        {
            return PrinterContentTypes.Jpeg;
        }

        if (extension == ".pdf")
        {
            return PrinterContentTypes.Pdf;
        }

        throw new NotSupportedException($"Files with extension '{extension}' are not supported.");
    }

    // The print files directory can hold a working file, such as a GIMP .xcf, that no
    // printer reads. Such a file must not appear in a menu.
    internal static bool CanPrint(string fileName)
    {
        try
        {
            _ = GetContentType(fileName);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    // A colour choice only means something for an image the printer renders.
    internal static bool IsImage(string contentType) =>
        contentType == PrinterContentTypes.Png || contentType == PrinterContentTypes.Jpeg;

    internal static string DescribeJobReading(PrintJobInfo reading)
    {
        StringBuilder line = new(reading.State.ToString());
        if (reading.ImpressionsCompleted is not null || reading.TotalImpressions is not null)
        {
            var total = reading.TotalImpressions is int totalImpressions ? totalImpressions.ToString(CultureInfo.InvariantCulture) : "?";
            line.Append($" {reading.ImpressionsCompleted ?? 0}/{total} pages");
        }

        if (reading.Detail is not null)
        {
            line.Append($"; {reading.Detail}");
        }

        return line.ToString();
    }

    internal static IReadOnlyList<string> GetLocalSubnetHosts()
    {
        HashSet<string> hosts = new(StringComparer.Ordinal);
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var addressBytes = unicast.Address.GetAddressBytes();
                if (IPAddress.IsLoopback(unicast.Address) || IsLinkLocal(addressBytes))
                {
                    continue;
                }

                if (!HasGateway(adapter))
                {
                    continue;
                }

                if (unicast.PrefixLength < 16 || unicast.PrefixLength > 30)
                {
                    continue;
                }

                var address = ReadUInt32(addressBytes);
                var mask = 0xFFFFFFFFu << (32 - unicast.PrefixLength);
                var network = address & mask;
                var broadcast = network | ~mask;
                for (var host = network + 1; host < broadcast && hosts.Count < MaxProbeHosts; host++)
                {
                    hosts.Add(ToAddress(host));
                }
            }
        }

        List<string> result = [.. hosts];
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool IsLinkLocal(byte[] addressBytes) => addressBytes[0] == 169 && addressBytes[1] == 254;

    private static bool HasGateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
        gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

    private static uint ReadUInt32(byte[] addressBytes) =>
        ((uint)addressBytes[0] << 24) | ((uint)addressBytes[1] << 16) | ((uint)addressBytes[2] << 8) | addressBytes[3];

    private static string ToAddress(uint address) => new IPAddress(
        [(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString();
}
