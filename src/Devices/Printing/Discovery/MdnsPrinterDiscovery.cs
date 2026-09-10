using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

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

        var channels = _channelFactory.Create(options);
        if (channels.Count == 0)
        {
            return [];
        }

        try
        {
            List<Task<List<ResourceRecord>>> browses = new(channels.Count);
            foreach ((var channel, var destination) in channels)
            {
                browses.Add(BrowseAsync(channel, destination, options, cancellationToken));
            }

            var results = await Task.WhenAll(browses).ConfigureAwait(false);
            List<ResourceRecord> records = [];
            foreach (var result in results)
            {
                records.AddRange(result);
            }

            return MdnsRecordAssembler.Assemble(records);
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
        CancellationToken cancellationToken)
    {
        using var windowSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowSource.CancelAfter(options.BrowseTimeout);

        var sender = SendQueriesAsync(channel, destination, options, windowSource.Token);
        var records = await ReceiveAsync(channel, options.MaxRecords, cancellationToken, windowSource.Token).ConfigureAwait(false);

        try
        {
            await sender.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The browse window ended mid-query.
        }
        catch (SocketException)
        {
            // The answers that already arrived are still valid.
        }

        cancellationToken.ThrowIfCancellationRequested();
        return records;
    }

    private static async Task<List<ResourceRecord>> ReceiveAsync(
        IUdpChannel channel,
        int maxRecords,
        CancellationToken cancellationToken,
        CancellationToken windowToken)
    {
        List<ResourceRecord> records = [];
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
                catch (InvalidDataException)
                {
                    // One responder that sends a malformed packet must not fail the browse.
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The end of the browse window is the normal finish.
        }
        catch (SocketException)
        {
            // Keep whatever arrived before the socket failed.
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
