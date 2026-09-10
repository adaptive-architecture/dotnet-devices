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

    /// <summary>
    /// Gets or sets a value indicating whether to read the capabilities of every channel
    /// that was found. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This opens each channel and asks the printer what it supports, which costs one
    /// request per channel. When it is off, <see cref="DiscoveredPrinter.Configuration"/>
    /// stays <c>null</c>, which means "not read" and not "nothing supported".
    /// </remarks>
    public bool ReadCapabilities { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to ask every channel what device it
    /// belongs to. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This is what merges the channels of one printer into a single
    /// <see cref="PrinterDevice"/> when the browse alone did not report an identity. It
    /// costs one request per channel: an IPP read for <c>printer-uuid</c>, an SNMP read
    /// for the serial number, and a spooler read for the device URI of a queue.
    /// </remarks>
    public bool ReadIdentity { get; set; }

    /// <summary>
    /// Gets or sets how many channels are read at once when
    /// <see cref="ReadCapabilities"/> or <see cref="ReadIdentity"/> is set. Defaults to
    /// eight.
    /// </summary>
    /// <remarks>
    /// A probe of a whole subnet can return hundreds of channels, and opening a session
    /// to every one of them at once would be a burst the network and the printers should
    /// not have to absorb.
    /// </remarks>
    public int MaxEnrichmentConcurrency { get; set; } = 8;
}
