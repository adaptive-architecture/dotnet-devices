namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Prints to and queries a printer installed in the operating system print spooler
/// (Win32 print queues, CUPS destinations).
/// </summary>
/// <remarks>
/// The configuration is read once and kept, so repeated calls to
/// <see cref="GetConfigurationAsync"/> cost nothing after the first.
/// </remarks>
public sealed class SpoolerPrinter : IPrinter
{
    private readonly ISpoolerDriver _driver;
    private readonly string _queueName;
    private PrinterConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrinter"/> class, using the
    /// spooler driver for the current operating system.
    /// </summary>
    /// <param name="endpoint">The spooler endpoint of the printer.</param>
    public SpoolerPrinter(SpoolerPrinterEndpoint endpoint)
        : this(endpoint, SpoolerDriverFactory.Create())
    {
    }

    internal SpoolerPrinter(SpoolerPrinterEndpoint endpoint, ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(driver);
        Endpoint = endpoint;
        _queueName = endpoint.Name;
        _driver = driver;
        Id = PrinterId.FromSpooler(_queueName);
        Info = new PrinterInfo(Id, _queueName);
    }

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
        IReadOnlyList<string> dropped = [];
        if (options is not null && options.OnUnsupported != UnsupportedOptionBehavior.Send)
        {
            var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            effectiveOptions = PrintOptionValidator.Apply(options, configuration, out dropped);
        }

        // The driver can drop options of its own, but never one already removed here.
        var job = await _driver.SubmitAsync(_queueName, payload, effectiveOptions, cancellationToken).ConfigureAwait(false);
        job.DroppedOptions = [.. dropped, .. job.DroppedOptions];
        return job;
    }

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
