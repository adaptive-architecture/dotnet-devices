namespace AdaptArch.Devices.Printing;

/// <summary>
/// Thrown when no IPP endpoint of a printer answered. <see cref="Failures"/> holds the cause
/// of each endpoint that was tried.
/// </summary>
/// <remarks>
/// The library tries IPPS (TLS) first and plain IPP next, across the caller path and the
/// well-known paths, which is up to six endpoints. Each one can fail for a different reason,
/// and the first one is usually the one that tells the truth: a TLS handshake that failed on
/// IPPS is more useful than the "connection refused" that a later plain IPP port gives.
/// <para>
/// This type derives from <see cref="InvalidOperationException"/>, which the printing API
/// documented before, so an existing <c>catch</c> block still catches it.
/// </para>
/// </remarks>
public sealed class PrinterConnectionException : InvalidOperationException
{
    private static readonly IReadOnlyDictionary<Uri, Exception> NoFailures = new Dictionary<Uri, Exception>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterConnectionException"/> class.
    /// </summary>
    public PrinterConnectionException()
        : this("A printer did not answer IPP.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PrinterConnectionException(string message)
        : base(message) => Failures = NoFailures;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public PrinterConnectionException(string message, Exception innerException)
        : base(message, innerException) => Failures = NoFailures;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterConnectionException"/> class.
    /// </summary>
    /// <param name="host">The printer host.</param>
    /// <param name="port">The printer port.</param>
    /// <param name="over">The transports that were tried, for example <c>IPPS or IPP</c>.</param>
    /// <param name="failures">The cause of each endpoint that was tried. Must hold at least one entry.</param>
    /// <param name="firstFailure">The cause of the first endpoint that was tried. It becomes the inner exception, because it is usually the one that names the true cause.</param>
    public PrinterConnectionException(string host, int port, string over, IReadOnlyDictionary<Uri, Exception> failures, Exception firstFailure)
        : base(BuildMessage(host, port, over, failures), firstFailure)
    {
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentOutOfRangeException.ThrowIfZero(failures.Count);
        Failures = failures;
    }

    /// <summary>
    /// Gets the cause of each endpoint that was tried. Read <see cref="Exception.InnerException"/>
    /// for the first one, which is usually the cause a user must act on.
    /// </summary>
    public IReadOnlyDictionary<Uri, Exception> Failures { get; }

    private static string BuildMessage(string host, int port, string over, IReadOnlyDictionary<Uri, Exception>? failures)
    {
        var head = $"Printer '{host}:{port}' did not answer IPP over {over}.";
        return failures is null || failures.Count == 0
            ? head
            : $"{head} Probes: {String.Join("; ", failures.Select(pair => $"{pair.Key}: {pair.Value.Message}"))}";
    }
}
