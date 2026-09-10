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

    /// <summary>
    /// The largest datagram that the channel reads: the most that fits in one IPv4 packet.
    /// An SNMP agent can answer a GetBulkRequest with a datagram of many kilobytes.
    /// </summary>
    private const int MaxDatagramSize = 65507;

    private readonly SnmpPrinterStatusOptions _options;
    private readonly Func<AddressFamily, IUdpChannel> _channelFactory;
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
        : this(options, static family => new UdpChannel(
            family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, MaxDatagramSize))
    {
    }

    internal SnmpPrinterStatusClient(SnmpPrinterStatusOptions options, Func<AddressFamily, IUdpChannel> channelFactory)
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
    /// <exception cref="InvalidOperationException">Thrown when the printer sends no answer, or reports an SNMP error status. A malformed datagram is not an answer, so a printer that sends only malformed datagrams also raises this.</exception>
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
        var maxRepetitions = MaxRepetitions;

        // One slot per table column, holding the row the walk has reached in it.
        List<(string Column, string Cursor)> slots = [];
        foreach (var column in PrinterMibOids.SupplyColumns)
        {
            slots.Add((column, column));
        }

        for (var round = 0; round < MaxWalkRounds && slots.Count > 0; round++)
        {
            List<string> cursors = [];
            foreach ((_, var cursor) in slots)
            {
                cursors.Add(cursor);
            }

            var reply = await BulkRequestAsync(destination, host, cursors, maxRepetitions, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                // The agent said tooBig. Ask for half as many rows, one time.
                maxRepetitions = Math.Max(1, maxRepetitions / 2);
                reply = await BulkRequestAsync(destination, host, cursors, maxRepetitions, cancellationToken).ConfigureAwait(false)
                    ?? throw new SnmpTooBigException();
            }

            var (advanced, stopped) = CollectRows(reply.Variables, slots, collected);

            // A slot that got no row has nothing more to give.
            for (var slot = slots.Count - 1; slot >= 0; slot--)
            {
                if (stopped[slot] || !advanced[slot])
                {
                    slots.RemoveAt(slot);
                }
            }
        }

        return collected;
    }

    // RFC 3416 §4.2.3: one variable per requested identifier per repetition, in order, so
    // variable i belongs to slot i modulo the slot count. A walk past the end of a column
    // spills into the next one, which the prefix test catches.
    private static (bool[] Advanced, bool[] Stopped) CollectRows(
        IReadOnlyList<Variable> variables,
        List<(string Column, string Cursor)> slots,
        List<Variable> collected)
    {
        var advanced = new bool[slots.Count];
        var stopped = new bool[slots.Count];
        for (var i = 0; i < variables.Count; i++)
        {
            var slot = i % slots.Count;
            if (stopped[slot])
            {
                continue;
            }

            var variable = variables[i];
            var oid = variable.Id.Oid;
            if (SnmpValues.IsEndOfView(variable.Data) || !IsRowOf(oid, slots[slot].Column))
            {
                stopped[slot] = true;
                continue;
            }

            collected.Add(variable);
            slots[slot] = (slots[slot].Column, oid);
            advanced[slot] = true;
        }

        return (advanced, stopped);
    }

    private static bool IsRowOf(string oid, string column) =>
        oid.Length > column.Length &&
        oid.StartsWith(column, StringComparison.Ordinal) &&
        oid[column.Length] == '.';

    // Returns null when the agent answered tooBig, so the caller can ask for less.
    private async Task<SnmpReply?> BulkRequestAsync(
        IPEndPoint destination,
        string host,
        IReadOnlyList<string> cursors,
        int maxRepetitions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RequestAsync(
                destination,
                host,
                requestId => SnmpMessages.CreateGetBulkRequest(_options.Community, requestId, cursors, maxRepetitions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SnmpTooBigException)
        {
            return null;
        }
    }

    private async Task<SnmpReply> RequestAsync(
        IPEndPoint destination,
        string host,
        Func<int, byte[]> createRequest,
        CancellationToken cancellationToken)
    {
        SocketException? lastFailure = null;
        for (var attempt = 0; attempt <= _options.Retries; attempt++)
        {
            var requestId = Interlocked.Increment(ref _requestId);
            var request = createRequest(requestId);
            try
            {
                var reply = await TrySendAsync(destination, request, requestId, cancellationToken).ConfigureAwait(false);
                if (reply is not null)
                {
                    return reply;
                }
            }
            catch (SocketException exception)
            {
                // Keep the cause, so the final error can name it.
                lastFailure = exception;
            }
        }

        throw new InvalidOperationException(
            $"Printer '{host}:{destination.Port}' did not answer SNMP queries after {_options.Retries + 1} attempts.",
            lastFailure);
    }

    private async Task<SnmpReply?> TrySendAsync(
        IPEndPoint destination,
        byte[] request,
        int requestId,
        CancellationToken cancellationToken)
    {
        using var channel = _channelFactory(destination.AddressFamily);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.RequestTimeout);
        try
        {
            await channel.SendAsync(request, destination, timeoutSource.Token).ConfigureAwait(false);
            while (true)
            {
                var result = await channel.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                if (!IsFrom(result.RemoteEndPoint, destination))
                {
                    continue;
                }

                SnmpReply reply;
                try
                {
                    reply = SnmpMessages.Parse(result.Buffer);
                }
                catch (InvalidDataException)
                {
                    // One malformed datagram must not end the attempt.
                    continue;
                }

                // A late answer to an earlier attempt carries an earlier identifier.
                if (reply.RequestId == requestId)
                {
                    return reply;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The attempt timed out; the caller decides whether to try again.
            return null;
        }
    }

    private static bool IsFrom(IPEndPoint remote, IPEndPoint destination)
    {
        var address = remote.Address;
        var expected = destination.Address;
        if (address.AddressFamily != expected.AddressFamily)
        {
            // An IPv6 socket reports an IPv4 sender as an IPv4-mapped IPv6 address.
            address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            expected = expected.IsIPv4MappedToIPv6 ? expected.MapToIPv4() : expected;
        }

        return address.Equals(expected);
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

        // Prefer IPv4, because most printers answer SNMP on IPv4 only.
        var chosen = Array.Find(addresses, static candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        return new IPEndPoint(chosen, port);
    }
}
