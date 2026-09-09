using System.Net;
using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Sends and receives UDP datagrams. This seam lets the discovery and status classes run
/// against a fake in unit tests, in the same way that the constructor overload of
/// <see cref="IppPrinterStatusClient"/> accepts a caller-supplied HTTP client.
/// </summary>
internal interface IUdpChannel : IDisposable
{
    /// <summary>
    /// Sends one datagram.
    /// </summary>
    /// <param name="datagram">The bytes to send.</param>
    /// <param name="destination">The address and port to send to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the datagram has been given to the network stack.</returns>
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint destination, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the next datagram.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The datagram and the address that sent it.</returns>
    ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken);
}
