using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Transmits raw payloads to <see cref="NetworkPrinterEndpoint"/> printers
/// over TCP, typically via the raw port 9100 channel.
/// </summary>
public sealed class TcpPrinterTransport : IPrinterTransport
{
    private readonly TimeSpan _connectTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPrinterTransport"/> class
    /// with a five second connection timeout.
    /// </summary>
    public TcpPrinterTransport()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPrinterTransport"/> class.
    /// </summary>
    /// <param name="connectTimeout">The timeout applied when establishing the TCP connection.</param>
    public TcpPrinterTransport(TimeSpan connectTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);
        _connectTimeout = connectTimeout;
    }

    /// <inheritdoc />
    public bool CanHandle(PrinterEndpoint endpoint) => endpoint is NetworkPrinterEndpoint;

    /// <inheritdoc />
    public async Task WriteAsync(PrinterEndpoint endpoint, PrinterPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(payload);
        if (endpoint is not NetworkPrinterEndpoint network)
        {
            throw new NotSupportedException($"Endpoint type '{endpoint.GetType().Name}' is not supported by TCP transport.");
        }

        using TcpClient client = new();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_connectTimeout);
        try
        {
            await client.ConnectAsync(network.Host, network.Port, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to printer '{network}'.", exception);
        }

        using var stream = client.GetStream();
        await stream.WriteAsync(payload.Data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
