namespace AdaptArch.Devices.Printing;

/// <summary>
/// The formats an application prints and the converters that widen them.
/// </summary>
/// <remarks>
/// Every content type the library knows is registered here, so an application adds a
/// format the same way the library declares one. A format that is not registered is
/// <see cref="PrinterFormatKind.Opaque"/>: it still prints, on a channel that sends the
/// bytes unchanged.
/// </remarks>
public sealed class PrintFormatPolicy
{
    // The formats the library itself reads. A content type that names no command set is
    // named by its media type in an IEEE 1284 report.
    private static readonly PrinterFormat[] BuiltIn =
    [
        new(PrinterContentTypes.Zpl, PrinterFormatKind.RawLanguage, "ZPL"),
        new(PrinterContentTypes.Epl, PrinterFormatKind.RawLanguage, "EPL"),
        new(PrinterContentTypes.Cpcl, PrinterFormatKind.RawLanguage),
        new(PrinterContentTypes.EscPos, PrinterFormatKind.RawLanguage),
        new(PrinterContentTypes.Pdf, PrinterFormatKind.Document, "PDF"),
        new(PrinterContentTypes.Png, PrinterFormatKind.Image, "PNG"),
        new(PrinterContentTypes.Jpeg, PrinterFormatKind.Image, "JPEG"),
    ];

    private static readonly List<IPrintPayloadConverter> ProcessConverters = [];
    private static readonly Lock ProcessLock = new();

    private readonly Dictionary<string, PrinterFormat> _formats;
    private readonly List<IPrintPayloadConverter> _converters;

    /// <summary>
    /// Gets the policy of an application that registered nothing of its own, plus every
    /// converter given to <see cref="AddDefaultConverter"/>.
    /// </summary>
    public static PrintFormatPolicy Default { get; } = new(null, null);

    /// <summary>
    /// Initializes a new instance of the <see cref="PrintFormatPolicy"/> class.
    /// </summary>
    /// <param name="formats">The formats to add to the built-in ones. A content type that is already known is replaced.</param>
    /// <param name="converters">The converters to add to the ones given to <see cref="AddDefaultConverter"/>.</param>
    public PrintFormatPolicy(IEnumerable<PrinterFormat>? formats, IEnumerable<IPrintPayloadConverter>? converters)
    {
        _formats = new Dictionary<string, PrinterFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var format in BuiltIn)
        {
            _formats[format.ContentType] = format;
        }

        if (formats is not null)
        {
            foreach (var format in formats)
            {
                ArgumentNullException.ThrowIfNull(format);
                _formats[format.ContentType] = format;
            }
        }

        _converters = [];
        if (converters is not null)
        {
            _converters.AddRange(converters);
        }
    }

    /// <summary>
    /// Adds a converter every printer of the process may use, for an application that
    /// builds no <see cref="PrinterManager"/> of its own.
    /// </summary>
    /// <param name="converter">The converter to add. A converter that is already registered is not added twice.</param>
    /// <remarks>
    /// <see cref="PrinterManagerOptions.Converters"/> is the way to register one for a
    /// single manager. This method is for a console application that opens a printer
    /// directly, and it is what
    /// <c>AdaptArch.Devices.Windows.WindowsPrinting.EnableSpoolerPdfPrinting()</c> calls.
    /// </remarks>
    public static void AddDefaultConverter(IPrintPayloadConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (ProcessLock)
        {
            if (!ProcessConverters.Contains(converter))
            {
                ProcessConverters.Add(converter);
            }
        }
    }

    /// <summary>
    /// Gets what a printer does with the content type.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns>The kind of the format, or <see cref="PrinterFormatKind.Opaque"/> when it is not registered.</returns>
    public PrinterFormatKind KindOf(string contentType) =>
        _formats.TryGetValue(contentType, out var format) ? format.Kind : PrinterFormatKind.Opaque;

    /// <summary>
    /// Tells whether the content type carries printer commands no server may rewrite.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns><c>true</c> for a printer language.</returns>
    public bool IsRawLanguage(string contentType) => KindOf(contentType) == PrinterFormatKind.RawLanguage;

    /// <summary>
    /// Gets the IEEE 1284 token a printer reports for the content type.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns>The token, or the content type itself when the format names none.</returns>
    public string CommandSetFor(string contentType) =>
        _formats.TryGetValue(contentType, out var format) ? format.CommandSet ?? contentType : contentType;

    /// <summary>
    /// Gets the converter that reads the content type.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns>The first converter that reads it, or <c>null</c> when none does.</returns>
    /// <remarks>
    /// A converter registered on the manager wins over one given to
    /// <see cref="AddDefaultConverter"/>, so an application can replace the built-in behaviour.
    /// </remarks>
    public IPrintPayloadConverter? ConverterFor(string contentType)
    {
        var own = _converters.Find(converter => converter.CanConvert(contentType));
        if (own is not null)
        {
            return own;
        }

        lock (ProcessLock)
        {
            return ProcessConverters.Find(converter => converter.CanConvert(contentType));
        }
    }
}
