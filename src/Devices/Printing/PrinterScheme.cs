namespace AdaptArch.Devices.Printing;

/// <summary>
/// Names the transport channel a <see cref="PrinterId"/> addresses. The scheme, not the
/// port, says how the bytes reach the device.
/// </summary>
/// <remarks>
/// This is the scheme of the identifier URI. It is deliberately not called a transport,
/// because <see cref="IPrinterTransport"/> already names the type that writes to a TCP
/// channel.
/// </remarks>
public enum PrinterScheme
{
    /// <summary>
    /// The raw TCP channel, normally port 9100. It sends the payload bytes unchanged and
    /// gives back no job identifier.
    /// </summary>
    Raw,

    /// <summary>
    /// The Internet Printing Protocol, normally port 631.
    /// </summary>
    Ipp,

    /// <summary>
    /// The Internet Printing Protocol over TLS, normally port 631.
    /// </summary>
    Ipps,

    /// <summary>
    /// A print queue of the operating system spooler.
    /// </summary>
    Spooler,

    /// <summary>
    /// A print queue of a CUPS server, addressed by host and queue name. This is the same
    /// daemon <see cref="Spooler"/> reaches on Linux and macOS, named over the network
    /// instead of on the loopback, so it is reachable from every operating system.
    /// </summary>
    Cups,
}
