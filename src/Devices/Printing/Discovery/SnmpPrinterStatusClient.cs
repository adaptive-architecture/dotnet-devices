using System.Net;
using System.Net.Sockets;
using DotNetSnmp.Asn1.SyntaxObjects;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reads identity and status details from network printers over SNMP version 2c.
/// It reads the Printer MIB of RFC 3805 and the Host Resources MIB, which many printers
/// supply even when they do not answer IPP. All operations are read-only.
/// </summary>
/// <remarks>
/// This client adds detail about a host that the caller already knows. It does not search
/// a network. Find printers with <see cref="IMdnsPrinterDiscovery"/> or
/// <see cref="INetworkPrinterDiscovery"/> first.
/// </remarks>
public sealed class SnmpPrinterStatusClient
{
    /// <summary>
    /// The UDP port that an SNMP agent listens on, as assigned by IANA.
    /// </summary>
    public const int DefaultPort = 161;

    /// <summary>
    /// How many rows of each table column one GetBulkRequest asks for. A printer normally
    /// has fewer than ten supplies, so one round trip is enough.
    /// </summary>
    private const int MaxRepetitions = 20;

    /// <summary>
    /// A limit on the rounds of a table walk, so a faulty agent cannot cause an endless loop.
    /// </summary>
    private const int MaxWalkRounds = 8;

    private readonly SnmpPrinterStatusOptions _options;
    private readonly Func<IUdpChannel> _channelFactory;
    private int _requestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpPrinterStatusClient"/> class with
    /// the default options and the community <c>public</c>.
    /// </summary>
    public SnmpPrinterStatusClient()
        : this(new SnmpPrinterStatusOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpPrinterStatusClient"/> class.
    /// </summary>
    /// <param name="options">The community string, the timeout, and the retry count.</param>
    public SnmpPrinterStatusClient(SnmpPrinterStatusOptions options)
        : this(options, static () => new UdpChannel(IPAddress.Any, 0))
    {
    }

    internal SnmpPrinterStatusClient(SnmpPrinterStatusOptions options, Func<IUdpChannel> channelFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.Community);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RequestTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(options.Retries);
        _options = options;
        _channelFactory = channelFactory;
        _requestId = Random.Shared.Next(1, Int32.MaxValue / 2);
    }

    /// <summary>
    /// Reads the identity, the status, and the supply levels of a printer.
    /// </summary>
    /// <param name="host">The printer host name or IP address.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="port">The SNMP port. Defaults to 161.</param>
    /// <returns>The printer details.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the printer sends no answer, or reports an SNMP error status.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed SNMP message.</exception>
    public async Task<SnmpPrinterDetails> GetDetailsAsync(string host, CancellationToken cancellationToken, int port = DefaultPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var destination = await ResolveAsync(host, port, cancellationToken).ConfigureAwait(false);
        var scalars = await RequestAsync(
            destination,
            host,
            requestId => SnmpMessages.CreateGetRequest(_options.Community, requestId, PrinterMibOids.Scalars),
            cancellationToken).ConfigureAwait(false);

        var supplies = await WalkSuppliesAsync(destination, host, cancellationToken).ConfigureAwait(false);
        return SnmpPrinterMapper.Map(host, scalars.Variables, supplies);
    }

    private async Task<IReadOnlyList<Variable>> WalkSuppliesAsync(
        IPEndPoint destination,
        string host,
        CancellationToken cancellationToken)
    {
        List<Variable> collected = [];
        List<string> cursors = [.. PrinterMibOids.SupplyColumns];

        for (var round = 0; round < MaxWalkRounds && cursors.Count > 0; round++)
        {
            var pending = cursors;
            var reply = await RequestAsync(
                destination,
                host,
                requestId => SnmpMessages.CreateGetBulkRequest(_options.Community, requestId, pending, MaxRepetitions),
                cancellationToken).ConfigureAwait(false);

            Dictionary<string, string> next = new(StringComparer.Ordinal);
            foreach (var variable in reply.Variables)
            {
                var oid = variable.Id.Oid;
                var column = FindColumn(pending, oid);
                if (column is null || SnmpValues.IsEndOfView(variable.Data))
                {
                    // This column has passed its last row, so the walk of it is finished.
                    continue;
                }

                collected.Add(variable);
                next[column] = oid;
            }

            if (next.Count == 0)
            {
                break;
            }

            cursors = [.. next.Values];
        }

        return collected;
    }

    private static string? FindColumn(IReadOnlyList<string> cursors, string oid)
    {
        // A cursor is either a bare column identifier or a row of that column, so the
        // column that owns a returned row is found by prefix.
        for (var i = 0; i < PrinterMibOids.SupplyColumns.Count; i++)
        {
            var column = PrinterMibOids.SupplyColumns[i];
            if (oid.Length > column.Length &&
                oid.StartsWith(column, StringComparison.Ordinal) &&
                oid[column.Length] == '.' &&
                IsWalked(cursors, column))
            {
                return column;
            }
        }

        return null;
    }

    private static bool IsWalked(IReadOnlyList<string> cursors, string column)
    {
        for (var i = 0; i < cursors.Count; i++)
        {
            if (cursors[i].StartsWith(column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<SnmpReply> RequestAsync(
        IPEndPoint destination,
        string host,
        Func<int, byte[]> createRequest,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= _options.Retries; attempt++)
        {
            var requestId = Interlocked.Increment(ref _requestId);
            var request = createRequest(requestId);
            var reply = await TrySendAsync(destination, request, requestId, cancellationToken).ConfigureAwait(false);
            if (reply is not null)
            {
                return reply;
            }
        }

        throw new InvalidOperationException(
            $"Printer '{host}:{destination.Port}' did not answer SNMP queries after {_options.Retries + 1} attempts.");
    }

    private async Task<SnmpReply?> TrySendAsync(
        IPEndPoint destination,
        byte[] request,
        int requestId,
        CancellationToken cancellationToken)
    {
        using var channel = _channelFactory();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.RequestTimeout);
        try
        {
            await channel.SendAsync(request, destination, timeoutSource.Token).ConfigureAwait(false);
            while (true)
            {
                var result = await channel.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                var reply = SnmpMessages.Parse(result.Buffer);

                // A late answer to an earlier attempt carries an earlier identifier.
                if (reply.RequestId == requestId)
                {
                    return reply;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The attempt timed out. The caller decides whether to try again.
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async Task<IPEndPoint> ResolveAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"The host '{host}' has no address.");
        }

        return new IPEndPoint(addresses[0], port);
    }
}
