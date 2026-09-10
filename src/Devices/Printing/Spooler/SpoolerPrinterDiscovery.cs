namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Finds printers installed in the operating system print spooler
/// (Win32 print queues, CUPS destinations).
/// </summary>
public sealed class SpoolerPrinterDiscovery : IPrinterDiscovery
{
    private readonly ISpoolerDriver _driver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrinterDiscovery"/> class, using
    /// the spooler driver for the current operating system.
    /// </summary>
    public SpoolerPrinterDiscovery()
        : this(SpoolerDriverFactory.Create())
    {
    }

    internal SpoolerPrinterDiscovery(ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken) =>
        _driver.EnumeratePrintersAsync(cancellationToken);
}
