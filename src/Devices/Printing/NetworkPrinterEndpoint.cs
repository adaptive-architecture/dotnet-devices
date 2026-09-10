namespace AdaptArch.Devices.Printing;

/// <summary>
/// Endpoint of a printer reachable over TCP: the raw channel, IPP, or IPP over TLS.
/// </summary>
/// <remarks>
/// The endpoint carries its scheme rather than letting the port imply it. A port says
/// nothing dependable: a printer can serve IPP on a port that is not 631, and reading the
/// channel back out of the number is how a raw channel and an IPP channel came to be
/// confused with each other.
/// </remarks>
public sealed class NetworkPrinterEndpoint : PrinterEndpoint, IEquatable<NetworkPrinterEndpoint>
{
    /// <summary>
    /// Default TCP port used for raw print data.
    /// </summary>
    public const int DefaultPort = 9100;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkPrinterEndpoint"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the printer.</param>
    /// <param name="scheme">The channel scheme. One of <see cref="PrinterScheme.Raw"/>, <see cref="PrinterScheme.Ipp"/> and <see cref="PrinterScheme.Ipps"/>.</param>
    /// <param name="port">The TCP port of the printer.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="host"/> is not a valid host name or address, or <paramref name="scheme"/> addresses no host.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="port"/> is outside 1 to 65535.</exception>
    public NetworkPrinterEndpoint(string host, PrinterScheme scheme, int port)
    {
        ThrowIfNotAHost(host);
        if (!PrinterSchemes.IsNetwork(scheme))
        {
            throw new ArgumentException($"'{PrinterSchemes.Format(scheme)}' does not address a host.", nameof(scheme));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Host = host;
        Scheme = scheme;
        Port = port;
    }

    /// <summary>
    /// Creates an endpoint for the raw TCP channel, which sends the payload unchanged.
    /// </summary>
    /// <param name="host">The host name or IP address of the printer.</param>
    /// <param name="port">The TCP port. Defaults to 9100.</param>
    public static NetworkPrinterEndpoint Raw(string host, int port = DefaultPort) =>
        new(host, PrinterScheme.Raw, port);

    /// <summary>
    /// Creates an endpoint for the IPP channel.
    /// </summary>
    /// <param name="host">The host name or IP address of the printer.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    public static NetworkPrinterEndpoint Ipp(string host, int port = IppPrinterStatusClient.DefaultPort) =>
        new(host, PrinterScheme.Ipp, port);

    /// <summary>
    /// Creates an endpoint for the IPP over TLS channel.
    /// </summary>
    /// <param name="host">The host name or IP address of the printer.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    public static NetworkPrinterEndpoint Ipps(string host, int port = IppPrinterStatusClient.DefaultPort) =>
        new(host, PrinterScheme.Ipps, port);

    /// <inheritdoc />
    public override PrinterScheme Scheme { get; }

    // A path, a query or user information in the value would change the target.
    internal static void ThrowIfNotAHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new ArgumentException($"'{host}' is not a valid host name or IP address.", nameof(host));
        }
    }

    /// <summary>
    /// Gets the host name or IP address of the printer.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the TCP port of the printer.
    /// </summary>
    public int Port { get; }

    /// <inheritdoc />
    public bool Equals(NetworkPrinterEndpoint? other) =>
        other is not null &&
        Scheme == other.Scheme &&
        Port == other.Port &&
        String.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine((int)Scheme, StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port);

    /// <inheritdoc />
    public override string ToString() =>
        $"{PrinterSchemes.Format(Scheme)}://{PrinterIdSyntax.FormatHost(Host)}:{Port}";
}
