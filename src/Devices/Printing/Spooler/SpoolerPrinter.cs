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
    private readonly string _queueName;
    private ISpoolerDriver? _driver;
    private PrinterConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrinter"/> class, using the
    /// spooler driver for the current operating system.
    /// </summary>
    /// <param name="endpoint">The spooler endpoint of the printer.</param>
    public SpoolerPrinter(SpoolerPrinterEndpoint endpoint)
        : this(endpoint, null)
    {
    }

    /// <summary>
    /// Gets the formats this printer knows and the converters it may use. Defaults to
    /// <see cref="PrintFormatPolicy.Default"/>.
    /// </summary>
    public PrintFormatPolicy Formats { get; init; } = PrintFormatPolicy.Default;

    // The driver is built on first use, because the formats are set by an object
    // initializer that runs after the constructor.
    private ISpoolerDriver Driver => _driver ??= SpoolerDriverFactory.Create(Formats);

    internal SpoolerPrinter(SpoolerPrinterEndpoint endpoint, ISpoolerDriver? driver)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
        _queueName = endpoint.Name;
        _driver = driver;
        Id = PrinterId.ForSpooler(_queueName);
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
        // A collection expression over two lists emits a compiler wrapper type that the
        // trimmer cannot keep intact, with no analyzer warning. A plain list is safe.
        var job = await Driver.SubmitAsync(_queueName, payload, effectiveOptions, cancellationToken).ConfigureAwait(false);
        List<string> allDropped = new(dropped.Count + job.DroppedOptions.Count);
        allDropped.AddRange(dropped);
        allDropped.AddRange(job.DroppedOptions);
        job.DroppedOptions = allDropped;
        return job;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A print queue is not a device. What it reports is the device it prints to, read
    /// from the CUPS device URI or the Windows port name.
    /// </remarks>
    public Task<PrinterIdentity?> GetIdentityAsync(CancellationToken cancellationToken) =>
        Driver.GetIdentityAsync(_queueName, cancellationToken);

    /// <inheritdoc />
    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Driver.GetStatusAsync(_queueName, cancellationToken);

    /// <inheritdoc />
    /// <remarks>The configuration is read once and the answer is kept for the life of this instance.</remarks>
    public async Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        var configuration = await Driver.GetConfigurationAsync(_queueName, cancellationToken).ConfigureAwait(false);
        _configuration = configuration;
        return configuration;
    }
}
