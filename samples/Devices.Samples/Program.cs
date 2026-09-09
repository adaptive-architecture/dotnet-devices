using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

const int MaxProbeHosts = 4096;

Console.WriteLine("AdaptArch.Devices samples");
Console.WriteLine($"Current OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

// Registrations from AdaptArch.Devices.DependencyInjection; the core package has zero runtime dependencies.
ServiceCollection services = new();
services.AddPrinters();
using var provider = services.BuildServiceProvider();

var printFilesDirectory = Path.Combine(AppContext.BaseDirectory, "PrintFiles");
Console.WriteLine("Printable files:");
foreach (var file in GetPrintFiles(printFilesDirectory))
{
    Console.WriteLine($"- {file}");
}

if (args.Length == 3 && args[0] == "--send")
{
    var target = args[2] == "*" ? "all files in PrintFiles" : $"'{args[2]}'";
    if (!Confirm($"Send {target} to printer {args[1]} on TCP port 9100?"))
    {
        Console.WriteLine("Cancelled; nothing was sent.");
        return;
    }

    if (args[2] == "*")
    {
        foreach (var file in GetPrintFiles(printFilesDirectory))
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

if (args.Length == 3 && args[0] == "--watch")
{
    if (!Confirm($"Send '{args[2]}' to printer {args[1]} and watch the job?"))
    {
        Console.WriteLine("Cancelled; nothing was sent.");
        return;
    }

    await WatchFileAsync(provider, printFilesDirectory, args[1], args[2]).ConfigureAwait(false);
    return;
}

// Ask the local network for printers that advertise themselves. One multicast query
// answers in about two seconds and opens no connection to any host.
var printers = await BrowseAsync(provider).ConfigureAwait(false);
if (printers.Count == 0)
{
    // No printer advertises itself, so fall back to a probe of every local address.
    printers = await ProbeAsync(provider).ConfigureAwait(false);
}

if (printers.Count == 0)
{
    Console.WriteLine("No printers found on the local network.");
}
else
{
    var ippClient = provider.GetRequiredService<IppPrinterStatusClient>();
    var snmpClient = provider.GetRequiredService<SnmpPrinterStatusClient>();
    foreach (var printer in printers)
    {
        Console.WriteLine($"- {printer.Info.Name} — {printer.Id.Value} ({printer.Endpoint})");
        if (printer.Endpoint is NetworkPrinterEndpoint network)
        {
            await PrintIppDetailsAsync(ippClient, network).ConfigureAwait(false);
            await PrintSnmpDetailsAsync(snmpClient, network).ConfigureAwait(false);
        }
    }
}

Console.WriteLine("Pass --send <host> <file> to transmit a file from PrintFiles over TCP port 9100 ('*' sends all files).");
Console.WriteLine("Pass --watch <host> <file> to send a file and follow its job progress.");

// Printing costs paper and ink, so the sample never transmits without an explicit "yes".
static bool Confirm(string question)
{
    Console.Write($"{question} [y/N]: ");
    var answer = Console.ReadLine();
    return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
}

static IReadOnlyList<string> GetPrintFiles(string directory)
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

static async Task SendFileAsync(ServiceProvider provider, string directory, string host, string fileName)
{
    var safeFileName = Path.GetFileName(fileName);
    var path = Path.Combine(directory, safeFileName);
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
        var data = await File.ReadAllBytesAsync(path, timeoutSource.Token).ConfigureAwait(false);
        if (data.Length == 0)
        {
            Console.WriteLine($"File '{safeFileName}' is empty; nothing to transmit.");
            return;
        }

        var payload = PrinterPayload.FromBytes(data, contentType);
        var transport = provider.GetRequiredService<IPrinterTransport>();
        await transport.WriteAsync(new NetworkPrinterEndpoint(host), payload, timeoutSource.Token).ConfigureAwait(false);
        Console.WriteLine($"Sent {data.Length} bytes ({contentType}) to {host}.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Send failed: {exception.Message}");
    }
}

static async Task WatchFileAsync(ServiceProvider provider, string directory, string host, string fileName)
{
    var safeFileName = Path.GetFileName(fileName);
    var path = Path.Combine(directory, safeFileName);
    if (!File.Exists(path))
    {
        Console.WriteLine($"File '{safeFileName}' not found in PrintFiles.");
        return;
    }

    // The printer factory is a DI singleton and owns the HttpClient every printer it
    // returns shares, so the printer this call gets back owns nothing and needs no
    // disposal here.
    var factory = provider.GetRequiredService<IPrinterFactory>();
    var monitor = provider.GetRequiredService<IPrintJobMonitor>();
    var printerId = PrinterId.FromNetwork(host);
    var printer = await factory.OpenAsync(printerId, CancellationToken.None).ConfigureAwait(false);
    var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
    var submitted = await printer.PrintAsync(
        PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
        new PrintOptions { JobName = safeFileName },
        CancellationToken.None).ConfigureAwait(false);

    Console.WriteLine($"Job {submitted.JobId} submitted.");

    // A network identifier without an IPP answer opens as a RawPrinter: the raw
    // channel has no job queue, so PrintAsync already reports the job Completed and
    // there is nothing left to watch.
    if (submitted.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
    {
        Console.WriteLine($"  {submitted.State}");
        return;
    }

    await foreach (var reading in monitor.WatchJobAsync(
        printerId, submitted.JobId, new PrintJobMonitorOptions(), CancellationToken.None).ConfigureAwait(false))
    {
        Console.WriteLine($"  {DescribeJobReading(reading)}");
    }
}

static string DescribeJobReading(PrintJobInfo reading)
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

static string GetContentType(string fileName)
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

static async Task<IReadOnlyList<DiscoveredPrinter>> BrowseAsync(ServiceProvider provider)
{
    Console.WriteLine("Asking the local network for printers over mDNS...");
    var discovery = provider.GetRequiredService<IMdnsPrinterDiscovery>();
    using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(30));
    try
    {
        return await discovery
            .DiscoverPrintersAsync(new MdnsPrinterDiscoveryOptions(), timeoutSource.Token).ConfigureAwait(false);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"The mDNS browse failed: {exception.Message}");
        return [];
    }
}

static async Task<IReadOnlyList<DiscoveredPrinter>> ProbeAsync(ServiceProvider provider)
{
    var hosts = GetLocalSubnetHosts();
    Console.WriteLine($"No printer answered. Probing {hosts.Count} local hosts on TCP port 9100...");
    var discovery = provider.GetRequiredService<INetworkPrinterDiscovery>();
    using CancellationTokenSource timeoutSource = new(TimeSpan.FromMinutes(2));
    NetworkPrinterDiscoveryOptions options = new()
    {
        Hosts = hosts,
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        MaxDegreeOfParallelism = 64,
    };

    try
    {
        return await discovery.DiscoverNetworkPrintersAsync(options, timeoutSource.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Probe timed out before completing.");
        return [];
    }
}

static async Task PrintIppDetailsAsync(IppPrinterStatusClient client, NetworkPrinterEndpoint endpoint)
{
    using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
    try
    {
        var details = await client.GetDetailsAsync(endpoint.Host, timeoutSource.Token).ConfigureAwait(false);
        StringBuilder line = new($"IPP: {details.Info.Name} — {details.Status.State}");
        if (details.Status.Detail is not null)
        {
            line.Append($"; {details.Status.Detail}");
        }

        AppendMarkers(line, details.Status.Markers);
        Console.WriteLine($"    {line}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"    IPP status unavailable: {exception.Message}");
    }
}

// SNMP reaches printers that supply the Printer MIB but do not answer IPP, and it reports
// the serial number and the page count, which IPP does not carry.
static async Task PrintSnmpDetailsAsync(SnmpPrinterStatusClient client, NetworkPrinterEndpoint endpoint)
{
    using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
    try
    {
        var details = await client.GetDetailsAsync(endpoint.Host, timeoutSource.Token).ConfigureAwait(false);
        StringBuilder line = new($"SNMP: {details.Info.Name} — {details.Status.State}");
        if (details.SerialNumber is not null)
        {
            line.Append($"; serial {details.SerialNumber}");
        }

        if (details.LifetimePageCount is not null)
        {
            line.Append($"; {details.LifetimePageCount} pages");
        }

        if (details.Status.Detail is not null)
        {
            line.Append($"; {details.Status.Detail}");
        }

        AppendMarkers(line, details.Status.Markers);
        Console.WriteLine($"    {line}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"    SNMP status unavailable: {exception.Message}");
    }
}

static void AppendMarkers(StringBuilder line, IReadOnlyList<PrinterMarker> markers)
{
    foreach (var marker in markers)
    {
        line.Append($"; {marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
    }
}

static IReadOnlyList<string> GetLocalSubnetHosts()
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

static bool IsLinkLocal(byte[] addressBytes) => addressBytes[0] == 169 && addressBytes[1] == 254;

static bool HasGateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

static uint ReadUInt32(byte[] addressBytes) =>
    ((uint)addressBytes[0] << 24) | ((uint)addressBytes[1] << 16) | ((uint)addressBytes[2] << 8) | addressBytes[3];

static string ToAddress(uint address) => new IPAddress(
    [(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString();
