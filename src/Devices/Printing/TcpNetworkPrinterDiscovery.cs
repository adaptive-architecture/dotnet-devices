using System.Collections.Concurrent;
using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Discovers network printers by probing hosts for an open raw print channel.
/// Probing is opt-in: callers pass the exact hosts to probe together with
/// timeouts and parallelism limits.
/// </summary>
public sealed class TcpNetworkPrinterDiscovery : INetworkPrinterDiscovery
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverNetworkPrintersAsync(NetworkPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Hosts);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDegreeOfParallelism, 1);

        ConcurrentBag<DiscoveredPrinter> found = new();
        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
        };
        await Parallel.ForEachAsync(options.Hosts, parallelOptions, async (host, hostCancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            if (await IsReachableAsync(host, options, hostCancellationToken).ConfigureAwait(false))
            {
                PrinterId id = new(PrinterIdKind.Network, host);
                NetworkPrinterEndpoint endpoint = new(host, options.Port);
                found.Add(new DiscoveredPrinter(id, endpoint, new PrinterInfo(id, host)));
            }
        }).ConfigureAwait(false);

        List<DiscoveredPrinter> result = [.. found];
        result.Sort(static (left, right) => string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal));
        return result;
    }

    private static async Task<bool> IsReachableAsync(string host, NetworkPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.ConnectTimeout);
        try
        {
            await client.ConnectAsync(host, options.Port, timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
