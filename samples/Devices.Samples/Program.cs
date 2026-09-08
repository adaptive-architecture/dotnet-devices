using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

const int MaxProbeHosts = 4096;

Console.WriteLine("AdaptArch.Devices samples");
Console.WriteLine($"Current OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

// Registrations from AdaptArch.Devices.DependencyInjection; the core package has zero runtime dependencies.
ServiceCollection services = new();
services.AddPrinters();
using ServiceProvider provider = services.BuildServiceProvider();

string printFilesDirectory = Path.Combine(AppContext.BaseDirectory, "PrintFiles");
Console.WriteLine("Printable files:");
foreach (string file in GetPrintFiles(printFilesDirectory))
{
    Console.WriteLine($"- {file}");
}

if (args.Length == 3 && args[0] == "--send")
{
    if (args[2] == "*")
    {
        foreach (string file in GetPrintFiles(printFilesDirectory))
        {
            await SendFileAsync(provider, printFilesDirectory, args[1], file).ConfigureAwait(false);
        }
    }
    else
    {
        await SendFileAsync(provider, printFilesDirectory, args[1], args[2]).ConfigureAwait(false);
    }

    return;
}

// Probe every host on the local subnets for an open raw print channel (TCP 9100).
IReadOnlyList<string> hosts = GetLocalSubnetHosts();
Console.WriteLine($"Probing {hosts.Count} local hosts for printers...");
INetworkPrinterDiscovery discovery = provider.GetRequiredService<INetworkPrinterDiscovery>();
using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
NetworkPrinterDiscoveryOptions options = new()
{
    Hosts = hosts,
    ConnectTimeout = TimeSpan.FromMilliseconds(500),
    MaxDegreeOfParallelism = 64,
};

try
{
    IReadOnlyList<DiscoveredPrinter> printers =
        await discovery.DiscoverNetworkPrintersAsync(options, timeoutSource.Token).ConfigureAwait(false);

    if (printers.Count == 0)
    {
        Console.WriteLine("No printers found on the local network.");
    }
    else
    {
        foreach (DiscoveredPrinter printer in printers)
        {
            Console.WriteLine($"- {printer.Id.Value} ({printer.Endpoint})");
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Probe timed out before completing.");
}

Console.WriteLine("Pass --send <host> <file> to transmit a file from PrintFiles over TCP port 9100 ('*' sends all files).");

static IReadOnlyList<string> GetPrintFiles(string directory)
{
    if (!Directory.Exists(directory))
    {
        return [];
    }

    List<string> files = [];
    foreach (string path in Directory.GetFiles(directory))
    {
        files.Add(Path.GetFileName(path));
    }

    files.Sort(StringComparer.Ordinal);
    return files;
}

static async Task SendFileAsync(ServiceProvider provider, string directory, string host, string fileName)
{
    string safeFileName = Path.GetFileName(fileName);
    string path = Path.Combine(directory, safeFileName);
    if (!File.Exists(path))
    {
        Console.WriteLine($"File '{safeFileName}' not found in PrintFiles.");
        return;
    }

    string contentType;
    try
    {
        contentType = GetContentType(safeFileName);
    }
    catch (NotSupportedException exception)
    {
        Console.WriteLine(exception.Message);
        return;
    }

    using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));
    try
    {
        byte[] data = await File.ReadAllBytesAsync(path, timeoutSource.Token).ConfigureAwait(false);
        if (data.Length == 0)
        {
            Console.WriteLine($"File '{safeFileName}' is empty; nothing to transmit.");
            return;
        }

        PrinterPayload payload = PrinterPayload.FromBytes(data, contentType);
        IPrinterTransport transport = provider.GetRequiredService<IPrinterTransport>();
        await transport.WriteAsync(new NetworkPrinterEndpoint(host), payload, timeoutSource.Token).ConfigureAwait(false);
        Console.WriteLine($"Sent {data.Length} bytes ({contentType}) to {host}.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Send failed: {exception.Message}");
    }
}

static string GetContentType(string fileName)
{
    string extension = Path.GetExtension(fileName).ToLowerInvariant();
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

static IReadOnlyList<string> GetLocalSubnetHosts()
{
    HashSet<string> hosts = new(StringComparer.Ordinal);
    foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (adapter.OperationalStatus != OperationalStatus.Up)
        {
            continue;
        }

        foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            byte[] addressBytes = unicast.Address.GetAddressBytes();
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

            uint address = ReadUInt32(addressBytes);
            uint mask = 0xFFFFFFFFu << (32 - unicast.PrefixLength);
            uint network = address & mask;
            uint broadcast = network | ~mask;
            for (uint host = network + 1; host < broadcast && hosts.Count < MaxProbeHosts; host++)
            {
                hosts.Add(ToAddress(host));
            }
        }
    }

    List<string> result = [.. hosts];
    result.Sort(StringComparer.Ordinal);
    return result;
}

static bool IsLinkLocal(byte[] addressBytes) => addressBytes[0] == 169 && addressBytes[1] == 254;

static bool HasGateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

static uint ReadUInt32(byte[] addressBytes) =>
    ((uint)addressBytes[0] << 24) | ((uint)addressBytes[1] << 16) | ((uint)addressBytes[2] << 8) | addressBytes[3];

static string ToAddress(uint address) => new IPAddress(
    [(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString();
