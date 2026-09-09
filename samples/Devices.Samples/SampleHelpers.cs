using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Samples;

// Helpers shared by more than one scenario: file listing and content-type lookup for
// PrintFiles, the print confirmation gate, job-reading formatting, and the local subnet
// sweep used when nothing answers mDNS.
internal static class SampleHelpers
{
    private const int MaxProbeHosts = 4096;

    // Printing costs paper and ink, so the sample never transmits without an explicit "yes".
    internal static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N]: ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
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

        if (extension == ".pdf")
        {
            return PrinterContentTypes.Pdf;
        }

        throw new NotSupportedException($"Files with extension '{extension}' are not supported.");
    }

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
