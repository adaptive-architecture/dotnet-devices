using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Finds printers installed in the operating system print spooler
/// (Win32 print queues, CUPS destinations).
/// </summary>
public sealed class SpoolerPrinterDiscovery : IPrinterDiscovery
{
    private ISpoolerDriver? _driver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrinterDiscovery"/> class, using
    /// the spooler driver for the current operating system.
    /// </summary>
    public SpoolerPrinterDiscovery()
    {
    }

    internal SpoolerPrinterDiscovery(ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
    }

    /// <summary>
    /// Gets the IPP policy of this printer: the log, the raw-response switch and the
    /// certificate trust. Defaults to <c>null</c>, which is the default policy.
    /// </summary>
    /// <remarks>
    /// It reaches the CUPS spooler, which speaks IPP. The Windows spooler is native interop
    /// and reads none of it.
    /// </remarks>
    public IppTransportOptions? IppTransport { get; init; }

    /// <summary>
    /// Gets the factory that makes the log. Defaults to <c>null</c>, which falls back to
    /// the factory of <see cref="IppTransport"/>, and then writes nothing.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    // The type holds an IppTransportOptions, so its factory is the fallback: a caller that
    // set only the transport policy still gets the log.
    private ILoggerFactory? EffectiveLoggerFactory => LoggerFactory ?? IppTransport?.LoggerFactory;

    // Built on first use, because an init property is set after the constructor runs.
    // LazyInitializer, not "??=": this type is registered as a singleton, and two concurrent
    // first calls must not each build a driver.
    private ISpoolerDriver Driver =>
        LazyInitializer.EnsureInitialized(ref _driver, () => SpoolerDriverFactory.Create(null, IppTransport, EffectiveLoggerFactory));

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken) =>
        Driver.EnumeratePrintersAsync(cancellationToken);
}
