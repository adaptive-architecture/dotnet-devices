namespace AdaptArch.Devices.Printing;

/// <summary>
/// One physical printer, with every channel that reaches it.
/// </summary>
/// <remarks>
/// A printer is normally reachable more than one way: it advertises IPP, it answers the
/// raw port, and the operating system also has a queue for it. Each of those is a
/// channel with its own identifier and its own capabilities. This type is what says the
/// three are one device.
/// <para>
/// The device is grouped by <see cref="Key"/>. When a source reported an identity — a
/// UUID or a serial number — the key is that identity and survives a change of address.
/// When none did, the key falls back to the address, and two channels group only when
/// they share it.
/// </para>
/// </remarks>
public sealed class PrinterDevice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDevice"/> class.
    /// </summary>
    /// <param name="key">The key that groups the channels.</param>
    /// <param name="channels">The channels that reach this device. At least one.</param>
    /// <param name="statusSources">The read-only protocols that answered about the device.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="channels"/> is empty.</exception>
    public PrinterDevice(PrinterDeviceKey key, IReadOnlyList<DiscoveredPrinter> channels, IReadOnlyList<PrinterStatusSource>? statusSources = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
        {
            throw new ArgumentException("A device has at least one channel.", nameof(channels));
        }

        Key = key;

        // Sorted once, so every caller iterates in the same order and the manager can
        // take the first channel that fits without sorting again.
        List<DiscoveredPrinter> ordered = [.. channels];
        ordered.Sort(Compare);
        Channels = ordered;

        Dictionary<PrinterScheme, IReadOnlyList<DiscoveredPrinter>> byTransport = [];
        foreach (var channel in ordered)
        {
            if (byTransport.TryGetValue(channel.Endpoint.Scheme, out var existing))
            {
                ((List<DiscoveredPrinter>)existing).Add(channel);
            }
            else
            {
                byTransport[channel.Endpoint.Scheme] = new List<DiscoveredPrinter> { channel };
            }
        }

        ChannelsByTransport = byTransport;
        Details = Merge(ordered, statusSources ?? []);
        Id = ordered.Find(static channel => channel.Id.IsDeviceIdentity)?.Id ?? ordered[0].Id;
    }

    /// <summary>
    /// Gets the key that groups the channels of this device.
    /// </summary>
    public PrinterDeviceKey Key { get; }

    /// <summary>
    /// Gets the identifier that best names this device: the identity form when a channel
    /// has one, and otherwise the identifier of the channel that is preferred first.
    /// </summary>
    public PrinterId Id { get; }

    /// <summary>
    /// Gets everything known about the device, gathered from every channel.
    /// </summary>
    public PrinterDeviceDetails Details { get; }

    /// <summary>
    /// Gets the channels that reach this device, most preferred first.
    /// </summary>
    /// <remarks>
    /// The order is the one the manager itself uses: the channels that carry a job queue
    /// come before the raw channel, because a job sent over a raw channel cannot be
    /// watched afterwards. Iterate this; use <see cref="ChannelsByTransport"/> to look one
    /// transport up.
    /// </remarks>
    public IReadOnlyList<DiscoveredPrinter> Channels { get; }

    /// <summary>
    /// Gets the channels of this device grouped by their transport.
    /// </summary>
    public IReadOnlyDictionary<PrinterScheme, IReadOnlyList<DiscoveredPrinter>> ChannelsByTransport { get; }

    /// <summary>
    /// Gets a value indicating whether any channel of this device carries a job queue, so
    /// that a job sent to it can be watched.
    /// </summary>
    public bool HasJobQueue
    {
        get
        {
            foreach (var channel in Channels)
            {
                if (channel.HasJobQueue)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether any channel of this device sends the payload bytes
    /// to it unchanged.
    /// </summary>
    public bool GivesPassthrough
    {
        get
        {
            foreach (var channel in Channels)
            {
                if (channel.GivesPassthrough)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Says whether a channel of this device reads a content type.
    /// </summary>
    /// <param name="channel">The channel the job would be sent over.</param>
    /// <param name="contentType">The content type of the payload, from <see cref="PrinterContentTypes"/>.</param>
    /// <returns>
    /// <c>true</c> when the channel reports the content type, <c>false</c> when it
    /// reports other content types only, and <c>null</c> when nothing was reported.
    /// </returns>
    /// <remarks>
    /// A channel that reports nothing did not refuse, so <c>null</c> is not a "no".
    /// <para>
    /// The judgement is per channel, never per device. The channels of one printer read
    /// different content types: an IPP service can take a JPEG that the raw port, which
    /// feeds the bytes straight to the page description language interpreter, cannot.
    /// </para>
    /// <para>
    /// Three sources answer, in this order: the <c>pdl</c> record of the advertisement,
    /// which the library keeps in <see cref="PrinterInfo.DriverName"/> and which names
    /// what this channel itself reads; then
    /// <see cref="PrinterConfiguration.SupportedDocumentFormats"/>, which comes from IPP
    /// <c>document-format-supported</c>; then <see cref="PrinterDeviceDetails.CommandSets"/>,
    /// which comes from the IEEE 1284 <c>CMD</c> field and belongs to the whole device.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="channel"/> is <c>null</c>.</exception>
    public bool? Accepts(DiscoveredPrinter channel, string contentType)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        // The "pdl" record names what this channel itself reads, so it answers before
        // anything the device says about itself as a whole.
        var advertised = PrinterDocumentFormats.Split(channel.Info.DriverName);
        if (advertised.Count > 0)
        {
            return PrinterDocumentFormats.Carries(advertised, contentType);
        }

        var formats = channel.Configuration?.SupportedDocumentFormats ?? [];
        if (formats.Count > 0)
        {
            return PrinterDocumentFormats.Carries(formats, contentType);
        }

        // IEEE 1284 names the languages the firmware reads. It belongs to the device, not
        // to one channel, so it is the last word and not the first.
        if (Details.CommandSets.Count == 0)
        {
            return null;
        }

        var commandSet = PrinterDocumentFormats.CommandSetFor(contentType);
        foreach (var command in Details.CommandSets)
        {
            if (command.Contains(commandSet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Details.Name} ({Key})";

    private static int Compare(DiscoveredPrinter left, DiscoveredPrinter right)
    {
        var byRank = PrinterSchemes.PreferenceRank(left.Endpoint.Scheme)
            .CompareTo(PrinterSchemes.PreferenceRank(right.Endpoint.Scheme));

        // The identifier breaks the tie, so the order never depends on which source
        // happened to answer first.
        return byRank != 0 ? byRank : String.CompareOrdinal(left.Id.ToString(), right.Id.ToString());
    }

    // A fact about the hardware comes from the channel that speaks to the device itself.
    private static int HardwareRank(PrinterScheme scheme) => PrinterSchemes.PreferenceRank(scheme);

    // A fact meant for a person comes from the spooler first: that is the text the
    // operating system already shows, and a device reports a model number instead.
    private static int HumanRank(PrinterScheme scheme) =>
        scheme == PrinterScheme.Spooler ? -1 : PrinterSchemes.PreferenceRank(scheme);

    private static PrinterDeviceDetails Merge(List<DiscoveredPrinter> channels, IReadOnlyList<PrinterStatusSource> statusSources)
    {
        List<DiscoveredPrinter> hardware = [.. channels];
        hardware.Sort(static (left, right) => HardwareRank(left.Endpoint.Scheme).CompareTo(HardwareRank(right.Endpoint.Scheme)));
        List<DiscoveredPrinter> human = [.. channels];
        human.Sort(static (left, right) => HumanRank(left.Endpoint.Scheme).CompareTo(HumanRank(right.Endpoint.Scheme)));

        List<DiscoverySource> sources = [];
        List<string> commandSets = [];
        var isDefault = false;
        var isShared = false;
        foreach (var channel in channels)
        {
            if (!sources.Contains(channel.Source))
            {
                sources.Add(channel.Source);
            }

            if (commandSets.Count == 0 && channel.Info.CommandSets.Count > 0)
            {
                commandSets = [.. channel.Info.CommandSets];
            }

            isDefault |= channel.Info.IsDefault;
            isShared |= channel.Info.IsShared;
        }

        return new PrinterDeviceDetails(First(human, static info => info.Name) ?? channels[0].Id.Authority)
        {
            Uuid = First(hardware, static info => info.Uuid),
            SerialNumber = First(hardware, static info => info.SerialNumber),
            Manufacturer = First(hardware, static info => info.Manufacturer),
            Model = First(hardware, static info => info.Model),
            Location = First(human, static info => info.Location),
            DriverName = First(human, static info => info.DriverName),
            CommandSets = commandSets,
            IsDefault = isDefault,
            IsShared = isShared,
            ContributedBy = sources,
            StatusSources = statusSources,
        };
    }

    private static string? First(List<DiscoveredPrinter> ordered, Func<PrinterInfo, string?> read)
    {
        foreach (var channel in ordered)
        {
            var value = read(channel.Info);
            if (!String.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
