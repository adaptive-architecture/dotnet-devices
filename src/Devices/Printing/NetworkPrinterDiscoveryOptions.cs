namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for direct network printer discovery via TCP probing.
/// Probing is opt-in by design: it opens connections to every listed host,
/// so callers control the scope, timeouts, and parallelism.
/// </summary>
public sealed class NetworkPrinterDiscoveryOptions
{
    /// <summary>
    /// Gets or sets the host names or IP addresses to probe.
    /// </summary>
    public IReadOnlyList<string> Hosts { get; set; } = [];

    /// <summary>
    /// Gets or sets the TCP port to probe. Defaults to 9100 (raw print channel).
    /// </summary>
    public int Port { get; set; } = NetworkPrinterEndpoint.DefaultPort;

    /// <summary>
    /// Gets or sets the channel the probe reports. Defaults to <see cref="PrinterScheme.Raw"/>,
    /// which matches the default port. Set it to <see cref="PrinterScheme.Ipp"/> when the
    /// probe targets the IPP port, so the identifier names the channel that answered.
    /// </summary>
    public PrinterScheme Scheme { get; set; } = PrinterScheme.Raw;

    /// <summary>
    /// Gets or sets the per-host connection timeout. Defaults to one second.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of concurrent probes. Defaults to 32.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 32;
}
