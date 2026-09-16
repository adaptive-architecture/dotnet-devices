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

    public IppContext(HttpClient httpClient, IppTransportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        Client = httpClient;
        Options = options ?? new IppTransportOptions();
        Logger = IppLog.Create(Options.LoggerFactory);
    }

    public HttpClient Client { get; }

    public IppTransportOptions Options { get; }

    // Never null: a caller that set no factory gets NullLogger, which returns at the
    // IsEnabled guard of every generated method.
    public ILogger Logger { get; }
}
