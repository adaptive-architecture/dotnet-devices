namespace AdaptArch.Devices.Printing;

/// <summary>
/// Names the discovery that reported a printer.
/// </summary>
public enum DiscoverySource
{
    /// <summary>
    /// The source is not known, or the printer was not reported by a discovery.
    /// </summary>
    Unknown,

    /// <summary>
    /// A multicast DNS browse of the local link.
    /// </summary>
    Mdns,

    /// <summary>
    /// A TCP probe of an explicit host list.
    /// </summary>
    NetworkProbe,

    /// <summary>
    /// The print queues of the operating system.
    /// </summary>
    Spooler,
}
