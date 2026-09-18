namespace AdaptArch.Devices.Printing;

/// <summary>
/// Endpoint of a print queue on a CUPS server, addressed by host, port and queue name.
/// </summary>
/// <remarks>
/// This is the queue a <see cref="SpoolerPrinterEndpoint"/> names on Linux and macOS,
/// reached over the network rather than on the loopback. It carries a host, so it is not a
/// <see cref="NetworkPrinterEndpoint"/>: that one addresses a device, and this one
/// addresses one queue of a server that may hold hundreds.
/// </remarks>
public sealed class CupsPrinterEndpoint : PrinterEndpoint, IEquatable<CupsPrinterEndpoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrinterEndpoint"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the CUPS server.</param>
    /// <param name="name">The queue name on that server.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    /// <exception cref="ArgumentException">Thrown when the host is not a host, or the name is not a valid queue name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1 to 65535.</exception>
    public CupsPrinterEndpoint(string host, string name, int port = IppPrinterStatusClient.DefaultPort)
    {
        NetworkPrinterEndpoint.ThrowIfNotAHost(host);

        // The queue rule is the one the spooler endpoint states, because the two name the
        // same thing on the same daemon.
        SpoolerPrinterEndpoint.ThrowIfNotAQueueName(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Host = host;
        Name = name;
        Port = port;
    }

    /// <inheritdoc />
    public override PrinterScheme Scheme => PrinterScheme.Cups;

    /// <summary>
    /// Gets the host name or IP address of the CUPS server.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the queue name on that server.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the TCP port of the CUPS server.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// The base address the IPP calls are made against.
    /// </summary>
    /// <remarks>
    /// The identifier names the queue and not the security of the channel, so the transport
    /// policy chooses between IPPS and plain IPP, by the same
    /// <see cref="IppTransportOptions.AllowPlainIpp"/> switch every other IPP channel reads.
    /// Nothing is probed here: unlike a printer, a CUPS server serves both on one port and
    /// answers whichever is asked for.
    /// </remarks>
    internal static Uri ServerUri(string host, int port, IppTransportOptions? options) =>
        new($"{((options?.AllowPlainIpp ?? true) ? "ipp" : "ipps")}://{PrinterIdSyntax.FormatHost(host)}:{port}/");

    /// <inheritdoc />
    /// <remarks>The host and the queue name are both compared without regard to case, as DNS and CUPS compare them.</remarks>
    public bool Equals(CupsPrinterEndpoint? other) =>
        other is not null &&
        Port == other.Port &&
        String.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) &&
        String.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CupsPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Host),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Name),
            Port);

    /// <inheritdoc />
    public override string ToString() => PrinterId.ForCups(Host, Name, Port).ToString();
}
