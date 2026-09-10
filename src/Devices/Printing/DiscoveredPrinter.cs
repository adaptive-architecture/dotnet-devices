namespace AdaptArch.Devices.Printing;

/// <summary>
/// One channel to a printer that a discovery found: its identifier, the endpoint it is
/// reached at, and what is known about it.
/// </summary>
/// <remarks>
/// A device usually has several of these. A network printer that advertises IPP and also
/// answers on the raw port gives two channels, and a queue in the operating system
/// spooler that prints to the same device gives a third.
/// <see cref="PrinterDevice"/> groups them.
/// </remarks>
public sealed class DiscoveredPrinter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveredPrinter"/> class.
    /// </summary>
    /// <param name="id">The channel identifier.</param>
    /// <param name="endpoint">The endpoint the channel is reached at.</param>
    /// <param name="info">Descriptive information about the printer.</param>
    public DiscoveredPrinter(PrinterId id, PrinterEndpoint endpoint, PrinterInfo info)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(info);
        Id = id;
        Endpoint = endpoint;
        Info = info;
        SupportedOptions = PrinterSchemes.SupportedOptions(endpoint.Scheme, OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    public PrinterId Id { get; }

    /// <summary>
    /// Gets the endpoint the channel is reached at.
    /// </summary>
    public PrinterEndpoint Endpoint { get; }

    /// <summary>
    /// Gets descriptive information about the printer.
    /// </summary>
    public PrinterInfo Info { get; }

    /// <summary>
    /// Gets the discovery that reported this channel.
    /// </summary>
    public DiscoverySource Source { get; init; }

    /// <summary>
    /// Gets the capabilities of this channel, or <c>null</c> when they were not read.
    /// </summary>
    /// <remarks>
    /// Reading them costs a request to the printer, so a discovery does it only when
    /// <see cref="PrinterManagerOptions.ReadCapabilities"/> is set. <c>null</c> means
    /// "not read", which is not the same as "the printer reported nothing".
    /// </remarks>
    public PrinterConfiguration? Configuration { get; init; }

    /// <summary>
    /// Gets the print options this channel applies to a job. Every other option is
    /// dropped or ignored, whatever the caller sets.
    /// </summary>
    /// <remarks>
    /// The value comes from the transport, which the library knows without asking, and is
    /// narrowed by <see cref="Configuration"/> when that was read.
    /// </remarks>
    public PrintOptionSupports SupportedOptions { get; init; }

    /// <summary>
    /// Gets the keys of the devices this channel was reported to belong to.
    /// </summary>
    /// <remarks>
    /// An alias is evidence, not a guess: the source that reported the channel stated
    /// that the channel and the key name the same physical device. A multicast DNS
    /// <c>UUID</c> record, an IPP <c>printer-uuid</c>, an SNMP serial number and a CUPS
    /// <c>device-uri</c> are the four that carry it. It is what merges a print queue with
    /// the network channels of the device behind it.
    /// </remarks>
    public IReadOnlyList<PrinterDeviceKey> Aliases { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a job sent over this channel can be watched
    /// afterwards. A raw channel gives back no job identifier, so it cannot.
    /// </summary>
    public bool HasJobQueue => PrinterSchemes.HasJobQueue(Endpoint.Scheme);

    /// <summary>
    /// Gets a value indicating whether this channel sends the payload bytes to the device
    /// unchanged.
    /// </summary>
    public bool GivesPassthrough => PrinterSchemes.GivesPassthrough(Endpoint.Scheme, OperatingSystem.IsWindows());

    /// <summary>
    /// Copies this channel with the capabilities and the identity a read supplied.
    /// </summary>
    /// <param name="id">The identifier, which becomes an identity form when the printer reported one.</param>
    /// <param name="info">The descriptive information, enriched by what the printer reported.</param>
    /// <param name="configuration">The capabilities, or <c>null</c> when they were not read.</param>
    /// <param name="aliases">The devices this channel was reported to belong to.</param>
    internal DiscoveredPrinter With(
        PrinterId id,
        PrinterInfo info,
        PrinterConfiguration? configuration,
        IReadOnlyList<PrinterDeviceKey> aliases) =>
        new(id, Endpoint, info)
        {
            Source = Source,
            Configuration = configuration,
            Aliases = aliases,
            SupportedOptions = Narrow(PrinterSchemes.SupportedOptions(Endpoint.Scheme, OperatingSystem.IsWindows()), configuration),
        };

    // A printer that reported nothing about a capability has not denied it, so only an
    // explicit "no" narrows the set. This is the rule PrintOptionValidator already uses.
    internal static PrintOptionSupports Narrow(PrintOptionSupports supported, PrinterConfiguration? configuration)
    {
        if (configuration is null)
        {
            return supported;
        }

        if (configuration.SupportsDuplex == false)
        {
            supported &= ~PrintOptionSupports.Duplex;
        }

        if (configuration.SupportsColor == false)
        {
            supported &= ~PrintOptionSupports.ColorMode;
        }

        if (configuration.SupportsPageRanges == false)
        {
            supported &= ~PrintOptionSupports.PageRanges;
        }

        return supported;
    }
}
