using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Transmits raw payloads to <see cref="NetworkPrinterEndpoint"/> printers
/// over TCP, typically via the raw port 9100 channel.
/// </summary>
public sealed class TcpPrinterTransport : IPrinterTransport
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPrinterTransport"/> class
    /// with a five second timeout.
    /// </summary>
    public TcpPrinterTransport()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPrinterTransport"/> class.
    /// </summary>
    /// <param name="connectTimeout">The time limit for the TCP connection, and then again for the write of the payload. A printer that accepts the connection but does not read the payload fails after this time.</param>
    public TcpPrinterTransport(TimeSpan connectTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);
        _timeout = connectTimeout;
    }

    /// <inheritdoc />
    public bool CanHandle(PrinterEndpoint endpoint) => endpoint is NetworkPrinterEndpoint;

    /// <inheritdoc />
    /// <exception cref="TimeoutException">Thrown when the connection or the write does not complete in the configured time.</exception>
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
        timeoutSource.CancelAfter(_timeout);
        try
        {
            await client.ConnectAsync(network.Host, network.Port, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to printer '{network}'.", exception);
        }

        // The write gets its own full time budget. A printer that stops reading, for
        // example one that is out of paper, would otherwise block the caller for ever.
        timeoutSource.CancelAfter(_timeout);
        await using var stream = client.GetStream();
        try
        {
            await stream.WriteAsync(payload.Data, timeoutSource.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out writing to printer '{network}'.", exception);
        }

        // Tell the printer that the job is complete. Some printers wait for this before
        // they print the last page.
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // The payload is already delivered. A failure to close one direction is not a failure to print.
        }
    }
}
