using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for <see cref="PrinterManager"/>: which discovery sources run, how much each
/// channel is asked, and which transports the manager may open.
/// </summary>
/// <remarks>
/// An instance given to the constructor is the policy of the manager, and every call
/// reads <see cref="Transports"/> from it. An instance given to
/// <see cref="IPrinterManager.DiscoverAsync"/> scopes that one discovery instead.
/// </remarks>
public sealed class PrinterManagerOptions
{
    /// <summary>
    /// Gets the transports a manager may open when the caller names none: IPPS, IPP, the
    /// spooler, then the raw channel.
    /// </summary>
    public static IReadOnlyList<PrinterScheme> DefaultTransports { get; } =
        [PrinterScheme.Ipps, PrinterScheme.Ipp, PrinterScheme.Spooler, PrinterScheme.Raw];

    /// <summary>
    /// Gets or sets the factory that makes the log of the printer manager, the routing and
    /// the job path. Defaults to <c>null</c>, which writes nothing. The log category is
    /// <c>AdaptArch.Devices.Printing</c>.
    /// </summary>
    /// <remarks>
    /// The IPP wire has its own <see cref="IppTransportOptions.LoggerFactory"/>. An
    /// application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs neither: the
    /// registration takes the <see cref="ILoggerFactory"/> of the container for both. Read
    /// <see href="https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md">Troubleshooting</see>.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; set; }

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
    /// Gets or sets the policy for proving that two network channels reach one print queue
    /// by comparing the jobs each of them reports. The correlation runs only when this is
    /// set, because it opens a session to every candidate channel. Defaults to <c>null</c>.
    /// </summary>
    /// <remarks>
    /// It runs after the enrichment and before the grouping, and contributes
    /// <see cref="DiscoveredPrinter.Aliases"/> exactly as an identity read does: two channels
    /// that report the same queue vouch for each other.
    /// <para>
    /// Only IPP and IPPS channels are candidates, and only ones no identity has already
    /// grouped, so setting <see cref="ReadIdentity"/> as well makes this cheaper and not
    /// dearer: a device that named itself is never asked about its queue.
    /// </para>
    /// <para>
    /// The manager reads this from the options it was built with, and never from the
    /// argument of <see cref="IPrinterManager.DiscoverAsync"/>. The correlation writes to a
    /// real printer when <see cref="QueueCorrelationOptions.AllowTracerJob"/> is set, and
    /// consent for that belongs to whoever built the manager, not to whoever scoped one
    /// discovery.
    /// </para>
    /// </remarks>
    public QueueCorrelationOptions? QueueCorrelation { get; set; }

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

    /// <summary>
    /// Gets or sets the transports the manager may open, most preferred first. Defaults to
    /// <see cref="DefaultTransports"/>, which is every transport.
    /// </summary>
    /// <remarks>
    /// Membership is permission: a channel on a transport this list leaves out is never
    /// opened, however the payload or the identifier would have ranked it. Set it to
    /// <c>[PrinterScheme.Spooler]</c> to print through the operating system only.
    /// <para>
    /// Position is preference, but only between the channels that suit the call equally.
    /// The payload still decides first — a printer language takes a channel that sends the
    /// bytes unchanged, and every other format takes a channel with a job queue — because
    /// an order that outranked the payload would send every label over IPP. Reorder this
    /// list to choose between two channels that both suit the call, such as a spooler queue
    /// and an IPP channel for a PDF.
    /// </para>
    /// <para>
    /// Discovery is not affected. A channel on a transport that is left out is still found,
    /// still listed in <see cref="PrinterDevice.Channels"/>, and still contributes what it
    /// reported to <see cref="PrinterDevice.Details"/>. This is what lets an application
    /// learn everything about a printer and still print through one transport.
    /// </para>
    /// <para>
    /// The manager reads this from the options it was built with, and never from the
    /// argument of <see cref="IPrinterManager.DiscoverAsync"/>: a print carries no options,
    /// and the scope of one discovery is not a policy for every call.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PrinterScheme> Transports { get; set; } = DefaultTransports;

    /// <summary>
    /// Gets the formats this manager knows beyond the built-in ones. Add a
    /// <see cref="PrinterFormat"/> to declare what a printer does with a content type the
    /// library does not know, such as <c>image/tiff</c>.
    /// </summary>
    /// <remarks>
    /// A content type that is already known is replaced, so an application can also
    /// correct a built-in entry. A format that is registered nowhere still prints, as
    /// <see cref="PrinterFormatKind.Opaque"/> bytes on a channel that sends them unchanged.
    /// </remarks>
    public IList<PrinterFormat> Formats { get; } = [];

    /// <summary>
    /// Gets the converters this manager may use to turn a format a channel cannot print
    /// into pages it can.
    /// </summary>
    /// <remarks>
    /// The first converter that reads the content type is the one that runs, and a
    /// converter here wins over one given to <see cref="PrintFormatPolicy.AddDefaultConverter"/>.
    /// </remarks>
    public IList<IPrintPayloadConverter> Converters { get; } = [];

    /// <summary>
    /// Builds the snapshot of <see cref="Formats"/> and <see cref="Converters"/> that a
    /// printer reads.
    /// </summary>
    /// <returns>The policy to give to <see cref="PrinterFactory.Formats"/>, or to a printer opened directly.</returns>
    /// <remarks>
    /// A manager takes this snapshot once, so a list edited afterwards does not change a
    /// job that is already on its way. Call it again to pick up later edits.
    /// </remarks>
    public PrintFormatPolicy BuildFormatPolicy() => new(Formats, Converters);
}
