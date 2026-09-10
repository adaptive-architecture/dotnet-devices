using System.Net;
using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// An <see cref="IUdpChannel"/> that uses a real socket. The socket binds an ephemeral
/// port, so it never competes with an operating system multicast DNS responder for
/// port 5353.
/// </summary>
internal sealed class UdpChannel : IUdpChannel
{
    private readonly Socket _socket;
    private readonly int _bufferSize;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpChannel"/> class for unicast requests.
    /// </summary>
    /// <param name="bindAddress">The local address to bind, normally <see cref="IPAddress.Any"/> or <see cref="IPAddress.IPv6Any"/>.</param>
    /// <param name="bufferSize">The largest datagram that the channel reads. A longer datagram is cut.</param>
    public UdpChannel(IPAddress bindAddress, int bufferSize)
        : this(bindAddress, bufferSize, 0, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpChannel"/> class.
    /// </summary>
    /// <param name="bindAddress">The local address to bind. Use the address of one interface to send multicast on that interface.</param>
    /// <param name="bufferSize">The largest datagram that the channel reads. A longer datagram is cut.</param>
    /// <param name="multicastTimeToLive">The multicast time to live. Pass zero for a unicast channel.</param>
    /// <param name="interfaceIndex">The index of the interface that sends IPv6 multicast. Not used for IPv4 or for unicast.</param>
    public UdpChannel(IPAddress bindAddress, int bufferSize, int multicastTimeToLive, int interfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bufferSize, 0);
        _bufferSize = bufferSize;
        _socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.Bind(new IPEndPoint(bindAddress, 0));
            if (multicastTimeToLive > 0)
            {
                SetMulticastOptions(bindAddress, multicastTimeToLive, interfaceIndex);
            }
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
    }

    private void SetMulticastOptions(IPAddress bindAddress, int multicastTimeToLive, int interfaceIndex)
    {
        if (bindAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, multicastTimeToLive);

            // Name the outgoing interface, so a host with several interfaces queries the
            // network of this address and not only the network of the default route.
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, bindAddress.GetAddressBytes());
            return;
        }

        // IPv6 names the outgoing interface by index. The scope identifier of the address
        // is not a substitute: it is zero for every address that is not link-local.
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, multicastTimeToLive);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, interfaceIndex);
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _ = await _socket.SendToAsync(datagram, SocketFlags.None, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_bufferSize];
        EndPoint remote = new IPEndPoint(
            _socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var result = await _socket
            .ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
        return new UdpReceiveResult(buffer[..result.ReceivedBytes], (IPEndPoint)result.RemoteEndPoint);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _socket.Dispose();
        }
    }
}
