using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Ipp;

// What every IPP call needs: the client that sends it, the policy that shapes it, and the
// log that records it. One object, because the logger and the capture switch travel to the
// same places and a parameter for each would reach through six call layers.
internal sealed class IppContext
{
    public IppContext(HttpClient httpClient)
        : this(httpClient, null)
    {
    }

    public IppContext(HttpClient httpClient, IppTransportOptions? options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        Client = httpClient;
        Options = options ?? new IppTransportOptions();
        Logger = IppLog.Create(loggerFactory ?? Options.LoggerFactory);
    }

    public HttpClient Client { get; }

    public IppTransportOptions Options { get; }

    // Never null: a caller that set no factory gets NullLogger, which returns at the
    // IsEnabled guard of every generated method.
    public ILogger Logger { get; }

    // Receives each document as it is submitted. Set by the printer that owns the context,
    // after its own init property is set.
    public IPrintCapture? Capture { get; set; }
}
