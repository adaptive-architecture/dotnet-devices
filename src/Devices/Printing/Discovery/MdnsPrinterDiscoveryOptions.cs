namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for printer discovery over multicast DNS service discovery.
/// The browse sends a query to the local link and collects the answers for a set time.
/// It does not open a connection to any host.
/// </summary>
public sealed class MdnsPrinterDiscoveryOptions
{
    /// <summary>
    /// The DNS-SD service type for the Internet Printing Protocol.
    /// </summary>
    public const string IppServiceType = "_ipp._tcp.local";

    /// <summary>
    /// The DNS-SD service type for the Internet Printing Protocol over TLS.
    /// </summary>
    public const string IppsServiceType = "_ipps._tcp.local";

    /// <summary>
    /// The DNS-SD service type for the raw print channel, which is normally TCP port 9100.
    /// </summary>
    public const string PdlDatastreamServiceType = "_pdl-datastream._tcp.local";

    /// <summary>
    /// The DNS-SD service type for the Line Printer Daemon protocol.
    /// </summary>
    public const string LpdServiceType = "_printer._tcp.local";

    /// <summary>
    /// Gets the service types that a browse asks for when the caller sets none.
    /// </summary>
    public static IReadOnlyList<string> DefaultServiceTypes { get; } =
        [PdlDatastreamServiceType, IppServiceType, IppsServiceType, LpdServiceType];

    /// <summary>
    /// Gets or sets the service types to ask for. Defaults to <see cref="DefaultServiceTypes"/>.
    /// </summary>
    public IReadOnlyList<string> ServiceTypes { get; set; } = DefaultServiceTypes;

    /// <summary>
    /// Gets or sets the time to collect answers. Defaults to two seconds. A printer
    /// normally answers in tens of milliseconds.
    /// </summary>
    public TimeSpan BrowseTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how many times to send the queries again during the browse.
    /// Defaults to two, because a multicast datagram can be lost.
    /// </summary>
    public int QueryRetries { get; set; } = 2;

    /// <summary>
    /// Gets or sets the indexes of the network interfaces to use. An empty list, which is
    /// the default, uses every interface that is up and has a usable address.
    /// </summary>
    public IReadOnlyList<int> NetworkInterfaceIndexes { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to query over IPv6 as well as IPv4.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool IncludeIPv6 { get; set; }

    /// <summary>
    /// Gets or sets the largest number of DNS records that the browse reads on one
    /// interface. When the count is reached, the browse stops reading on that interface.
    /// This bounds the memory that a flood of answers can take. Defaults to 10000.
    /// </summary>
    public int MaxRecords { get; set; } = 10000;
}
