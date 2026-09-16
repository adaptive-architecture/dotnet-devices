namespace AdaptArch.Devices.Printing;

/// <summary>
/// Thrown when a printer answered a request, but reported a failure. The properties carry
/// the printer, the endpoint and the IPP status code as data, so a caller can act on them
/// without a match on the message text.
/// </summary>
/// <remarks>
/// This type derives from <see cref="InvalidOperationException"/>, which the printing API
/// documented before, so an existing <c>catch</c> block still catches it.
/// </remarks>
public sealed class PrinterOperationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterOperationException"/> class.
    /// </summary>
    public PrinterOperationException()
        : this("A printer reported a failure.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterOperationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PrinterOperationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterOperationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public PrinterOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the printer that reported the failure, or <c>null</c> when the operation ran
    /// before a printer identifier was known.
    /// </summary>
    public PrinterId? PrinterId { get; init; }

    /// <summary>
    /// Gets the endpoint that answered, or <c>null</c> when none did.
    /// </summary>
    public Uri? Endpoint { get; init; }

    /// <summary>
    /// Gets the name of the IPP operation, for example <c>Print-Job</c>, or <c>null</c> when
    /// it is not known.
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    /// Gets the IPP status code the printer reported, or <c>null</c> when the answer carried
    /// none. The value is the code of RFC 8011 section 13.1, for example <c>0x0406</c> for
    /// <c>client-error-not-found</c> and <c>0x040A</c> for
    /// <c>client-error-document-format-not-supported</c>.
    /// </summary>
    public int? IppStatusCode { get; init; }

    /// <summary>
    /// Gets the attributes of the raw IPP answer. Empty unless
    /// <see cref="IppTransportOptions.CaptureRawResponses"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<IppAttributeSnapshot> RawAttributes { get; init; } = [];
}
