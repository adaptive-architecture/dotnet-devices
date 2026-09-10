namespace AdaptArch.Devices.Printing;

/// <summary>
/// Prints to a printer that offers only the raw TCP port 9100 channel, with no job
/// tracking and no guaranteed status channel.
/// </summary>
/// <remarks>
/// <see cref="GetStatusAsync"/> probes SNMP and then IPP for a status, because a printer
/// that only accepts raw print data often still answers one of those two channels for
/// status. <see cref="GetConfigurationAsync"/> always reports an empty configuration,
/// because the raw channel offers no way to ask a printer what it supports.
/// </remarks>
public sealed class RawPrinter : IPrinter
{
    // IPrinter is not IDisposable, so a client per instance would have no owner.
    private static readonly Lazy<IppPrinterStatusClient> SharedIppClient = new(static () => new IppPrinterStatusClient());

    private readonly TcpPrinterTransport _transport;
    private readonly string _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawPrinter"/> class with the default
    /// <see cref="SnmpPrinterStatusOptions"/> for the SNMP probe in
    /// <see cref="GetStatusAsync"/>.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    public RawPrinter(NetworkPrinterEndpoint endpoint)
        : this(endpoint, new SnmpPrinterStatusOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RawPrinter"/> class.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="snmpOptions">
    /// The options for the SNMP probe in <see cref="GetStatusAsync"/>: the community
    /// string, the per-attempt timeout, and the retry count. Pass a short timeout and no
    /// retries when the host is known to have no SNMP agent, so the probe fails fast.
    /// </param>
    public RawPrinter(NetworkPrinterEndpoint endpoint, SnmpPrinterStatusOptions snmpOptions)
        : this(endpoint, new SnmpPrinterStatusClient(snmpOptions), SharedIppClient.Value)
    {
    }

    internal RawPrinter(NetworkPrinterEndpoint endpoint, SnmpPrinterStatusClient snmpClient, IppPrinterStatusClient ippClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(snmpClient);
        ArgumentNullException.ThrowIfNull(ippClient);
        Endpoint = endpoint;
        _host = endpoint.Host;
        Id = PrinterId.FromNetwork(_host);
        Info = new PrinterInfo(Id, _host);
        _transport = new TcpPrinterTransport();
        SnmpStatusClient = snmpClient;
        IppStatusClient = ippClient;
    }

    /// <inheritdoc />
    public PrinterId Id { get; }

    /// <inheritdoc />
    public PrinterEndpoint Endpoint { get; }

    /// <inheritdoc />
    public PrinterInfo Info { get; }

    internal SnmpPrinterStatusClient SnmpStatusClient { get; }

    internal IppPrinterStatusClient IppStatusClient { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The raw channel gives back no job identifier, so the returned job is already
    /// <see cref="PrintJobState.Completed"/> with a generated identifier as soon as the
    /// bytes are transmitted.
    /// </remarks>
    public async Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await _transport.WriteAsync(Endpoint, payload, cancellationToken).ConfigureAwait(false);

        var completedAt = DateTimeOffset.UtcNow;
        return new PrintJobInfo(Guid.NewGuid().ToString("n"), Id, PrintJobState.Completed)
        {
            JobName = options?.JobName,
            CompletedAt = completedAt,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tries SNMP first, then IPP, and reports <see cref="PrinterStatusState.Unknown"/> when
    /// neither answers.
    /// </remarks>
    public async Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snmpDetails = await SnmpStatusClient.GetDetailsAsync(_host, cancellationToken).ConfigureAwait(false);
            return snmpDetails.Status;
        }
        catch (InvalidOperationException)
        {
            // SNMP did not answer, so try IPP.
        }

        try
        {
            var ippDetails = await IppStatusClient.GetDetailsAsync(_host, cancellationToken).ConfigureAwait(false);
            return ippDetails.Status;
        }
        catch (InvalidOperationException)
        {
            // Neither status channel answered.
        }

        return new PrinterStatus(Id, PrinterStatusState.Unknown);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always reports an empty configuration, meaning "not known": the raw channel offers
    /// no way to ask a printer what it supports.
    /// </remarks>
    public Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterConfiguration(Id));
}
