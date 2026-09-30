using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Prints to and queries a print queue of a CUPS server reached over the network.
/// </summary>
/// <remarks>
/// This is <see cref="SpoolerPrinter"/> pointed at another host. The daemon is the same, so
/// the calls are the same IPP operations; only the address differs, which is what makes a
/// CUPS queue reachable from Windows, from a container, and from a machine with no spooler
/// of its own.
/// <para>
/// The configuration is read once and kept, so repeated calls to
/// <see cref="GetConfigurationAsync"/> cost nothing after the first.
/// </para>
/// </remarks>
public sealed class CupsPrinter : IPrinter
{
    private readonly ISpoolerDriver _driver;
    private readonly string _queueName;
    private readonly ILoggerFactory? _transportLoggerFactory;
    private PrinterConfiguration? _configuration;
    private ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrinter"/> class.
    /// </summary>
    /// <param name="endpoint">The queue and the server it is on.</param>
    /// <param name="httpClient">
    /// The HTTP client used to send IPP requests. Not disposed by this instance. The library
    /// treats the client as one that validates certificates, so a failed TLS handshake
    /// throws instead of a fallback to plain IPP.
    /// </param>
    public CupsPrinter(CupsPrinterEndpoint endpoint, HttpClient httpClient)
        : this(endpoint, httpClient, IppTransportOptions.ForSuppliedClient(), null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrinter"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="Ipp.IppHttpClientFactory.Create"/>.
    /// </summary>
    /// <param name="endpoint">The queue and the server it is on.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the credentials, the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    /// <param name="formats">The formats this printer knows and the converters it may use, or <c>null</c> for <see cref="PrintFormatPolicy.Default"/>.</param>
    public CupsPrinter(
        CupsPrinterEndpoint endpoint,
        HttpClient httpClient,
        IppTransportOptions options,
        PrintFormatPolicy? formats)
        : this(endpoint, CreateDriver(endpoint, httpClient, options, formats))
    {
        _transportLoggerFactory = options.LoggerFactory;
    }

    private static CupsSpoolerDriver CreateDriver(
        CupsPrinterEndpoint endpoint,
        HttpClient httpClient,
        IppTransportOptions options,
        PrintFormatPolicy? formats)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return CupsSpoolerDriver.ForServer(endpoint.Host, endpoint.Port, httpClient, options, formats);
    }

    internal CupsPrinter(CupsPrinterEndpoint endpoint, ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(driver);
        Endpoint = endpoint;
        _driver = driver;
        _queueName = endpoint.Name;
        Id = PrinterId.ForCups(endpoint.Host, endpoint.Name, endpoint.Port);
        Info = new PrinterInfo(Id, endpoint.Name);
    }

    /// <summary>
    /// Gets the factory that makes the log of the options a job lost. Defaults to
    /// <c>null</c>, which falls back to the factory of the <see cref="IppTransportOptions"/>
    /// this printer was built with, and then writes nothing.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    private ILogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => PrintingLog.Create(LoggerFactory ?? _transportLoggerFactory));

    /// <inheritdoc />
    public PrinterId Id { get; }

    /// <inheritdoc />
    public PrinterEndpoint Endpoint { get; }

    /// <inheritdoc />
    public PrinterInfo Info { get; }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown when an option is not supported by the printer and <see cref="PrintOptions.OnUnsupported"/> is <see cref="UnsupportedOptionBehavior.Throw"/>.</exception>
    public async Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var effectiveOptions = options;
        IReadOnlyList<DroppedOption> dropped = [];
        if (options is not null && options.OnUnsupported != UnsupportedOptionBehavior.Send)
        {
            var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            effectiveOptions = PrintOptionValidator.Apply(options, configuration, out dropped);
        }

        var job = await _driver.SubmitAsync(_queueName, payload, effectiveOptions, cancellationToken).ConfigureAwait(false);
        PrintOptionValidator.Prepend(job, dropped);
        PrintingLog.ReportDropped(Logger, job);
        return job;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A print queue is not a device. What it reports is the device it prints to, read from
    /// the CUPS device URI.
    /// </remarks>
    public Task<PrinterIdentity?> GetIdentityAsync(CancellationToken cancellationToken) =>
        _driver.GetIdentityAsync(_queueName, cancellationToken);

    /// <inheritdoc />
    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _driver.GetStatusAsync(_queueName, cancellationToken);

    /// <inheritdoc />
    /// <remarks>The configuration is read once and the answer is kept for the life of this instance.</remarks>
    public async Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        var configuration = await _driver.GetConfigurationAsync(_queueName, cancellationToken).ConfigureAwait(false);
        _configuration = configuration;
        return configuration;
    }
}
