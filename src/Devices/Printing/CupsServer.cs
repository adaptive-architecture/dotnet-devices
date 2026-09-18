namespace AdaptArch.Devices.Printing;

/// <summary>
/// Names a CUPS server whose print queues an application wants discovered.
/// </summary>
/// <remarks>
/// A CUPS server announces nothing on the local link, so it is found only when it is named.
/// The credentials and the certificate trust are not here: they are transport policy, and
/// they live on <see cref="IppTransportOptions"/> with every other IPP connection. Point
/// <see cref="IppTransportOptions.Credentials"/> at a
/// <see cref="System.Net.CredentialCache"/> to give two servers two passwords.
/// </remarks>
public sealed class CupsServer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CupsServer"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the server.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    /// <exception cref="ArgumentException">Thrown when the host is not a host.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1 to 65535.</exception>
    public CupsServer(string host, int port = IppPrinterStatusClient.DefaultPort)
    {
        NetworkPrinterEndpoint.ThrowIfNotAHost(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Host = host;
        Port = port;
    }

    /// <summary>
    /// Gets the host name or IP address of the server.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the TCP port of the server.
    /// </summary>
    public int Port { get; }

    /// <inheritdoc />
    public override string ToString() => $"{PrinterIdSyntax.FormatHost(Host)}:{Port}";
}
