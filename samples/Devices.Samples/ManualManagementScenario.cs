using System.Text;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples;

// Everything here bypasses IPrinterManager and calls the seams it sits on: a transport
// resolved by hand, and the mDNS and TCP discovery sources run one at a time. This is not
// duplication of PrinterManagerScenario; it is the point of the scenario, because it shows
// the layer the manager is built on.
internal static class ManualManagementScenario
{
    internal static async Task DiscoverAsync(ServiceProvider provider)
    {
        Console.WriteLine("Asking the local network for printers over mDNS...");
        var mdns = provider.GetRequiredService<IMdnsPrinterDiscovery>();
        IReadOnlyList<DiscoveredPrinter> printers;
        using (CancellationTokenSource mdnsTimeout = new(TimeSpan.FromSeconds(30)))
        {
            try
            {
                printers = await mdns.DiscoverPrintersAsync(new MdnsPrinterDiscoveryOptions(), mdnsTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"The mDNS browse failed: {exception.Message}");
                printers = [];
            }
        }

        if (printers.Count == 0)
        {
            var hosts = SampleHelpers.GetLocalSubnetHosts();
            Console.WriteLine($"No printer answered mDNS. Probing {hosts.Count} local hosts on TCP port 9100...");
            var network = provider.GetRequiredService<INetworkPrinterDiscovery>();
            NetworkPrinterDiscoveryOptions options = new()
            {
                Hosts = hosts,
                ConnectTimeout = TimeSpan.FromMilliseconds(500),
                MaxDegreeOfParallelism = 64,
            };

            using CancellationTokenSource probeTimeout = new(TimeSpan.FromMinutes(2));
            try
            {
                printers = await network.DiscoverNetworkPrintersAsync(options, probeTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Probe timed out before completing.");
                printers = [];
            }
        }

        if (printers.Count == 0)
        {
            Console.WriteLine("No printers found on the local network.");
            return;
        }

        var ippClient = provider.GetRequiredService<IppPrinterStatusClient>();
        var snmpClient = provider.GetRequiredService<SnmpPrinterStatusClient>();
        foreach (var printer in printers)
        {
            Console.WriteLine($"- {printer.Info.Name} — {printer.Id.Value} ({printer.Endpoint}) [{printer.Source}]");
            if (printer.Endpoint is NetworkPrinterEndpoint network)
            {
                await PrintIppDetailsAsync(ippClient, network).ConfigureAwait(false);
                await PrintSnmpDetailsAsync(snmpClient, network).ConfigureAwait(false);
            }
        }
    }

    internal static async Task StatusAsync(ServiceProvider provider, string host)
    {
        var endpoint = new NetworkPrinterEndpoint(host);
        var ippClient = provider.GetRequiredService<IppPrinterStatusClient>();
        var snmpClient = provider.GetRequiredService<SnmpPrinterStatusClient>();
        await PrintIppDetailsAsync(ippClient, endpoint).ConfigureAwait(false);
        await PrintSnmpDetailsAsync(snmpClient, endpoint).ConfigureAwait(false);
    }

    internal static async Task SendAsync(ServiceProvider provider, string directory, string host, string fileName)
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
            contentType = SampleHelpers.GetContentType(safeFileName);
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

    private static async Task PrintIppDetailsAsync(IppPrinterStatusClient client, NetworkPrinterEndpoint endpoint)
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
    private static async Task PrintSnmpDetailsAsync(SnmpPrinterStatusClient client, NetworkPrinterEndpoint endpoint)
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

    private static void AppendMarkers(StringBuilder line, IReadOnlyList<PrinterMarker> markers)
    {
        foreach (var marker in markers)
        {
            line.Append($"; {marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
        }
    }
}
