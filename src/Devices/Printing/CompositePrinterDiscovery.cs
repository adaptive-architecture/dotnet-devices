namespace AdaptArch.Devices.Printing;

/// <summary>
/// Runs several printer discoveries at once and reports what all of them found.
/// </summary>
/// <remarks>
/// This is what puts the local spooler and the CUPS servers an application named behind
/// the one <see cref="IPrinterDiscovery"/> that <see cref="PrinterManager"/> asks. A member
/// that fails does not fail the call, by the same rule the manager applies to its own
/// sources: an empty answer is an answer, and a print server that is down must not hide the
/// queues of the machine itself. Only a failure of every member throws, and it throws the
/// first failure.
/// </remarks>
public sealed class CompositePrinterDiscovery : IPrinterDiscovery
{
    private readonly IReadOnlyList<IPrinterDiscovery> _sources;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePrinterDiscovery"/> class.
    /// </summary>
    /// <param name="sources">The discoveries to run. At least one.</param>
    /// <exception cref="ArgumentException">Thrown when no source is given.</exception>
    public CompositePrinterDiscovery(params IPrinterDiscovery[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("A composite discovery needs at least one source.", nameof(sources));
        }

        _sources = [.. sources];
    }

    /// <inheritdoc />
    /// <exception cref="Exception">The failure of the first source, thrown when every source failed.</exception>
    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(_sources.Select(source => RunAsync(source, cancellationToken))).ConfigureAwait(false);

        List<DiscoveredPrinter> found = [];
        Exception? firstFailure = null;
        var failures = 0;
        foreach ((var printers, var error) in results)
        {
            if (error is null)
            {
                found.AddRange(printers);
                continue;
            }

            failures++;
            firstFailure ??= error;
        }

        return failures == results.Length ? throw firstFailure! : found;
    }

    private static async Task<(IReadOnlyList<DiscoveredPrinter> Printers, Exception? Error)> RunAsync(
        IPrinterDiscovery source, CancellationToken cancellationToken)
    {
        try
        {
            return (await source.DiscoverAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ([], exception);
        }
    }
}
