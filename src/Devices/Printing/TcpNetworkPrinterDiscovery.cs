using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Discovers network printers by probing hosts for an open raw print channel.
/// Probing is opt-in: callers pass the exact hosts to probe together with
/// timeouts and parallelism limits.
/// </summary>
public sealed class TcpNetworkPrinterDiscovery : INetworkPrinterDiscovery
{
    private ILogger? _logger;

    /// <summary>
    /// Gets the factory that makes the log. Defaults to <c>null</c>, which writes nothing.
    /// The log category is <c>AdaptArch.Devices.Printing</c>.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// Read <see href="https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md">Troubleshooting</see>.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    // Built on first use: an init property is set after the constructor runs.
    private ILogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => PrintingLog.Create(LoggerFactory));

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(NetworkPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Hosts);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDegreeOfParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ConnectTimeout, TimeSpan.Zero);

        ConcurrentBag<DiscoveredPrinter> found = new();
        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
        };

        var logger = Logger;
        List<string> hosts = [.. options.Hosts.Distinct(StringComparer.OrdinalIgnoreCase)];
        await Parallel.ForEachAsync(hosts, parallelOptions, async (host, hostCancellationToken) =>
        {
            if (String.IsNullOrWhiteSpace(host))
            {
                return;
            }

            if (await IsReachableAsync(host, options, logger, hostCancellationToken).ConfigureAwait(false))
            {
                var id = PrinterId.ForNetwork(options.Scheme, host, options.Port);
                NetworkPrinterEndpoint endpoint = new(host, options.Scheme, options.Port);
                found.Add(new DiscoveredPrinter(id, endpoint, new PrinterInfo(id, host)) { Source = DiscoverySource.NetworkProbe });
            }
        }).ConfigureAwait(false);

        List<DiscoveredPrinter> result = [.. found];
        result.Sort(static (left, right) => String.CompareOrdinal(left.Id.ToString(), right.Id.ToString()));
        DiscoveryLog.ProbeCompleted(logger, hosts.Count, options.Port, result.Count);
        return result;
    }

    private static async Task<bool> IsReachableAsync(string host, NetworkPrinterDiscoveryOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.ConnectTimeout);
        try
        {
            await client.ConnectAsync(host, options.Port, timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException exception)
        {
            // Refused and timed out are the same "false" to a caller, and a different
            // answer to a user: one is nothing listening, the other is a dropped packet.
            DiscoveryLog.ProbeRefused(logger, host, options.Port, exception);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DiscoveryLog.ProbeTimedOut(logger, host, options.Port);
            return false;
        }
    }
}
