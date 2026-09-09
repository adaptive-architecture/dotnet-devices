namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for <see cref="IPrinterManager.DiscoverAsync"/>, scoping which discovery
/// sources run and how.
/// </summary>
public sealed class PrinterManagerOptions
{
    /// <summary>
    /// Gets or sets the options for the multicast DNS discovery source.
    /// Defaults to <c>new()</c>. The browse timeout is set through <see cref="MdnsPrinterDiscoveryOptions.BrowseTimeout"/>
    /// on this property, not on <see cref="PrinterManagerOptions"/>.
    /// </summary>
    public MdnsPrinterDiscoveryOptions Mdns { get; set; } = new();

    /// <summary>
    /// Gets or sets the options for the network probe discovery source. The probe runs
    /// only when this is set, because it opens connections to the hosts it lists.
    /// Defaults to <c>null</c>.
    /// </summary>
    public NetworkPrinterDiscoveryOptions? Probe { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include the operating system print
    /// spooler as a discovery source. Defaults to <c>true</c>.
    /// </summary>
    public bool IncludeSpooler { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to include the multicast DNS browse as a
    /// discovery source. Defaults to <c>true</c>. Set this to <c>false</c> to skip the
    /// browse entirely; to shorten it instead, set <see cref="MdnsPrinterDiscoveryOptions.BrowseTimeout"/>
    /// on <see cref="Mdns"/>.
    /// </summary>
    public bool IncludeMdns { get; set; } = true;
}
