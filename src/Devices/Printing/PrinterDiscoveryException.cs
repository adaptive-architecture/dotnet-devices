namespace AdaptArch.Devices.Printing;

/// <summary>
/// Thrown by <see cref="IPrinterManager.DiscoverAsync"/> when every configured discovery
/// source failed. <see cref="Failures"/> holds the error of each source.
/// </summary>
public sealed class PrinterDiscoveryException : Exception
{
    private static readonly IReadOnlyDictionary<DiscoverySource, Exception> NoFailures = new Dictionary<DiscoverySource, Exception>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDiscoveryException"/> class.
    /// </summary>
    public PrinterDiscoveryException()
        : this(BuildMessage(null))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDiscoveryException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PrinterDiscoveryException(string message)
        : base(message) => Failures = NoFailures;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDiscoveryException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public PrinterDiscoveryException(string message, Exception innerException)
        : base(message, innerException) => Failures = NoFailures;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDiscoveryException"/> class.
    /// </summary>
    /// <param name="failures">The error of each source that failed. Must hold at least one entry.</param>
    public PrinterDiscoveryException(IReadOnlyDictionary<DiscoverySource, Exception> failures)
        : base(BuildMessage(failures), failures?.Values.FirstOrDefault())
    {
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentOutOfRangeException.ThrowIfZero(failures.Count);
        Failures = failures;
    }

    /// <summary>
    /// Gets the error of each source that failed.
    /// </summary>
    public IReadOnlyDictionary<DiscoverySource, Exception> Failures { get; }

    private static string BuildMessage(IReadOnlyDictionary<DiscoverySource, Exception>? failures) =>
        failures is null || failures.Count == 0
            ? "Every printer discovery source failed."
            : $"Every printer discovery source failed: {String.Join("; ", failures.Select(pair => $"{pair.Key}: {pair.Value.Message}"))}";
}
