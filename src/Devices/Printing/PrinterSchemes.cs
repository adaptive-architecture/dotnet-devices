namespace AdaptArch.Devices.Printing;

// Every rule that depends only on the scheme lives here, so the library never has to
// guess a channel from a port number again. The methods are if-chains rather than switch
// expressions to match the style of the rest of the printing code.
internal static class PrinterSchemes
{
    private const string RawText = "raw";
    private const string IppText = "ipp";
    private const string IppsText = "ipps";
    private const string SpoolerText = "spooler";

    // A scheme with no network port reports zero.
    public const int NoPort = 0;

    public static string Format(PrinterScheme scheme)
    {
        if (scheme == PrinterScheme.Raw)
        {
            return RawText;
        }

        if (scheme == PrinterScheme.Ipp)
        {
            return IppText;
        }

        if (scheme == PrinterScheme.Ipps)
        {
            return IppsText;
        }

        if (scheme == PrinterScheme.Spooler)
        {
            return SpoolerText;
        }

        throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown printer scheme.");
    }

    // Compared without allocating, because Enum.Parse is neither trim-safe nor free.
    public static bool TryParse(ReadOnlySpan<char> text, out PrinterScheme scheme)
    {
        if (text.Equals(RawText, StringComparison.OrdinalIgnoreCase))
        {
            scheme = PrinterScheme.Raw;
            return true;
        }

        if (text.Equals(IppText, StringComparison.OrdinalIgnoreCase))
        {
            scheme = PrinterScheme.Ipp;
            return true;
        }

        if (text.Equals(IppsText, StringComparison.OrdinalIgnoreCase))
        {
            scheme = PrinterScheme.Ipps;
            return true;
        }

        if (text.Equals(SpoolerText, StringComparison.OrdinalIgnoreCase))
        {
            scheme = PrinterScheme.Spooler;
            return true;
        }

        scheme = default;
        return false;
    }

    /// <summary>
    /// The port a scheme uses when the identifier names none, or <see cref="NoPort"/> for
    /// a scheme that has no network port.
    /// </summary>
    public static int DefaultPort(PrinterScheme scheme)
    {
        if (scheme == PrinterScheme.Raw)
        {
            return NetworkPrinterEndpoint.DefaultPort;
        }

        if (scheme is PrinterScheme.Ipp or PrinterScheme.Ipps)
        {
            return IppPrinterStatusClient.DefaultPort;
        }

        return NoPort;
    }

    /// <summary>
    /// Whether the scheme addresses a host and a port.
    /// </summary>
    public static bool IsNetwork(PrinterScheme scheme) =>
        scheme is PrinterScheme.Raw or PrinterScheme.Ipp or PrinterScheme.Ipps;

    /// <summary>
    /// Whether a job sent over this scheme can be found again and watched. A raw channel
    /// gives back no job identifier, so it cannot.
    /// </summary>
    public static bool HasJobQueue(PrinterScheme scheme) =>
        scheme is PrinterScheme.Ipp or PrinterScheme.Ipps or PrinterScheme.Spooler;

    /// <summary>
    /// Whether the scheme sends the payload bytes to the device unchanged.
    /// </summary>
    /// <remarks>
    /// The raw channel always does, and so does the Windows spooler with the RAW data
    /// type. CUPS does not give the promise, even though the library submits a printer
    /// language as <c>application/vnd.cups-raw</c>: that format is necessary but not
    /// sufficient, because a queue with a driver still converts the job, and CUPS exposes
    /// no dependable attribute that tells such a queue from a raw one. The platform is a
    /// parameter so both branches are testable.
    /// </remarks>
    public static bool GivesPassthrough(PrinterScheme scheme, bool isWindows)
    {
        if (scheme == PrinterScheme.Raw)
        {
            return true;
        }

        if (scheme == PrinterScheme.Spooler)
        {
            return isWindows;
        }

        return false;
    }

    /// <summary>
    /// The print options a channel applies, judged from the transport alone.
    /// </summary>
    /// <remarks>
    /// A raw channel writes the payload to a socket and applies nothing. The Windows
    /// spooler driver maps what a device mode can hold, and reports every other option in
    /// <see cref="PrintJobInfo.DroppedOptions"/>. CUPS and IPP carry the whole set as job
    /// template attributes. Reading the capabilities of a printer can narrow this further,
    /// but never widen it.
    /// </remarks>
    public static PrintOptionSupports SupportedOptions(PrinterScheme scheme, bool isWindows)
    {
        if (scheme == PrinterScheme.Raw)
        {
            return PrintOptionSupports.None;
        }

        if (scheme == PrinterScheme.Spooler)
        {
            return isWindows ? WindowsDeviceModeOptions : PrintOptionSupports.All;
        }

        return PrintOptionSupports.All;
    }

    // What a Windows device mode carries, plus the copies the driver prints as one
    // document each. MediaType would need a DMMEDIA_* number, which the spooler does not
    // pair with a name, and OutputBin, PageRanges and NumberUp have no device mode field.
    // Scaling is named here because the scale field carries PrintScaling.None; the fit
    // modes have no field, and the device mode mapper drops them.
    private const PrintOptionSupports WindowsDeviceModeOptions =
        PrintOptionSupports.JobName | PrintOptionSupports.Copies | PrintOptionSupports.Duplex
        | PrintOptionSupports.ColorMode | PrintOptionSupports.Orientation | PrintOptionSupports.MediaSource
        | PrintOptionSupports.MediaSize | PrintOptionSupports.ResolutionDpi | PrintOptionSupports.Quality
        | PrintOptionSupports.Scaling;

    /// <summary>
    /// The order in which the manager considers the channels of one device. A lower
    /// number is considered first.
    /// </summary>
    /// <remarks>
    /// The two channels that carry a job queue come first, so a job can be watched after
    /// it is sent. The raw channel comes after them, because it reports nothing back.
    /// </remarks>
    public static int PreferenceRank(PrinterScheme scheme)
    {
        if (scheme == PrinterScheme.Ipps)
        {
            return 0;
        }

        if (scheme == PrinterScheme.Ipp)
        {
            return 1;
        }

        if (scheme == PrinterScheme.Spooler)
        {
            return 2;
        }

        return Int32.MaxValue;
    }
}
