namespace AdaptArch.Devices.Printing;

/// <summary>
/// Endpoint of a printer reachable over TCP, typically via the raw port 9100 channel.
/// </summary>
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
    /// <param name="port">The TCP port of the printer. Defaults to 9100.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="host"/> is not a valid host name or address.</exception>
    public NetworkPrinterEndpoint(string host, int port = DefaultPort)
    {
        ThrowIfNotAHost(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Host = host;
        Port = port;
    }

    /// <inheritdoc />
    public override PrinterIdKind Kind => PrinterIdKind.Network;

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
        Port == other.Port &&
        String.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port);

    /// <inheritdoc />
    public override string ToString() => $"{Host}:{Port}";
}
