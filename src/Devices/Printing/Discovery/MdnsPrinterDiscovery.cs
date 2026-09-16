using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Finds printers with a one-shot multicast DNS browse.
/// </summary>
/// <remarks>
/// The socket binds an ephemeral port and not port 5353. RFC 6762 §5.1 and §6.7 name a
/// querier that sends from another port a "one-shot" querier, and they require each
/// responder to answer it by unicast. The browse therefore needs no share of port 5353
/// with an operating system responder such as avahi or Bonjour.
/// </remarks>
public sealed class MdnsPrinterDiscovery : IMdnsPrinterDiscovery
{
    private readonly IMdnsChannelFactory _channelFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MdnsPrinterDiscovery"/> class.
    /// </summary>
    public MdnsPrinterDiscovery()
        : this(new MdnsChannelFactory())
    {
    }

    internal MdnsPrinterDiscovery(IMdnsChannelFactory channelFactory) => _channelFactory = channelFactory;

    private ILogger? _logger;

    /// <summary>
    /// Gets the factory that makes the log. Defaults to <c>null</c>, which writes nothing.
    /// The log category is <c>AdaptArch.Devices.Printing.Discovery</c>.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// Read <see href="https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md">Troubleshooting</see>.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    // Built on first use: an init property is set after the constructor runs.
    private ILogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => DiscoveryLog.Create(LoggerFactory));

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(MdnsPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ServiceTypes);
        ArgumentNullException.ThrowIfNull(options.NetworkInterfaceIndexes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.BrowseTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QueryRetries, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxRecords, 0);

        if (options.ServiceTypes.Count == 0)
        {
            return [];
        }

        var channels = _channelFactory.Create(options, Logger);
        if (channels.Count == 0)
        {
            // "No printers" and "no network interface this browse can use" look the same to
            // a caller, and they need different answers.
            DiscoveryLog.NoUsableInterface(Logger);
            return [];
        }

        try
        {
            List<Task<List<ResourceRecord>>> browses = new(channels.Count);
            foreach ((var channel, var destination) in channels)
            {
                browses.Add(BrowseAsync(channel, destination, options, Logger, cancellationToken));
            }

            var results = await Task.WhenAll(browses).ConfigureAwait(false);
            List<ResourceRecord> records = [];
            foreach (var result in results)
            {
                records.AddRange(result);
            }

            var printers = MdnsRecordAssembler.Assemble(records);
            DiscoveryLog.BrowseCompleted(Logger, records.Count, channels.Count, printers.Count);
            return printers;
        }
        finally
        {
            foreach ((var channel, _) in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static async Task<List<ResourceRecord>> BrowseAsync(
        IUdpChannel channel,
        IPEndPoint destination,
        MdnsPrinterDiscoveryOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var windowSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowSource.CancelAfter(options.BrowseTimeout);

        var sender = SendQueriesAsync(channel, destination, options, windowSource.Token);
        var records = await ReceiveAsync(channel, options.MaxRecords, logger, cancellationToken, windowSource.Token).ConfigureAwait(false);

        try
        {
            await sender.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The browse window ended mid-query.
        }
        catch (SocketException exception)
        {
            // The answers that already arrived are still valid.
            DiscoveryLog.BrowseChannelFailed(logger, records.Count, exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return records;
    }

    private static async Task<List<ResourceRecord>> ReceiveAsync(
        IUdpChannel channel,
        int maxRecords,
        ILogger logger,
        CancellationToken cancellationToken,
        CancellationToken windowToken)
    {
        List<ResourceRecord> records = [];
        MalformedPacketReporter malformed = new();
        try
        {
            // The cap bounds the memory a flood of answers can take.
            while (records.Count < maxRecords)
            {
                var result = await channel.ReceiveAsync(windowToken).ConfigureAwait(false);
                try
                {
                    records.AddRange(MdnsMessages.ReadRecords(result.Buffer));
                }
                catch (InvalidDataException exception)
                {
                    // One responder that sends a malformed packet must not fail the browse.
                    if (malformed.ShouldReport(result.RemoteEndPoint))
                    {
                        DiscoveryLog.MalformedMdnsPacket(logger, result.RemoteEndPoint, exception);
                    }
                }
            }

            DiscoveryLog.BrowseTruncated(logger, maxRecords);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The end of the browse window is the normal finish.
        }
        catch (SocketException exception)
        {
            // Keep whatever arrived before the socket failed.
            DiscoveryLog.BrowseChannelFailed(logger, records.Count, exception);
        }

        if (malformed.HasMore)
        {
            DiscoveryLog.MalformedMdnsPacketsSkipped(logger, malformed.Count, malformed.SenderCount);
        }

        return records;
    }

    private static async Task SendQueriesAsync(
        IUdpChannel channel,
        IPEndPoint destination,
        MdnsPrinterDiscoveryOptions options,
        CancellationToken windowToken)
    {
        var rounds = options.QueryRetries + 1;

        // The rounds are spread out: only queries sent apart survive a lost datagram.
        var delay = options.BrowseTimeout / rounds;
        for (var round = 0; round < rounds; round++)
        {
            if (round > 0)
            {
                await Task.Delay(delay, windowToken).ConfigureAwait(false);
            }

            for (var i = 0; i < options.ServiceTypes.Count; i++)
            {
                var serviceType = options.ServiceTypes[i];
                if (String.IsNullOrWhiteSpace(serviceType))
                {
                    continue;
                }

                var query = MdnsMessages.CreatePtrQuery(serviceType);
                await channel.SendAsync(query, destination, windowToken).ConfigureAwait(false);
            }
        }
    }
}
