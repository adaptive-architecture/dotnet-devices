using System.Net.Sockets;
using Microsoft.Extensions.Logging;

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

    private ILogger? _logger;

    /// <summary>
    /// Gets the factory that makes the log. Defaults to <c>null</c>, which writes nothing.
    /// The log category is <c>AdaptArch.Devices.Printing</c>.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// Read <see href="https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md">Troubleshooting</see>.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    // Built on first use: an init property is set after the constructor runs.
    private ILogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => PrintingLog.Create(LoggerFactory));

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

        // Its own budget: a printer that stops reading must not block the caller for ever.
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

        // Some printers wait for this before they print the last page.
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // The payload is already delivered, so a failed close is not a failed print.
            // Error even so: a raw job reports Completed the moment the bytes go out, so
            // this entry is the only evidence that the last page may be missing.
            PrintingLog.RawShutdownFailed(Logger, network.Host, network.Port, payload.Data.Length, exception);
            return;
        }

        PrintingLog.RawJobWritten(Logger, payload.Data.Length, network.Host, network.Port);
    }
}
