using System.Linq;
using System.Security.Authentication;
using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// The IPP path differs between makers, so the probe tries the caller path, then the
// well-known paths, over TLS and then plain. The answer is kept until a transport failure.
internal sealed class IppEndpointResolver
{
    private static readonly string[] WellKnownPaths = ["/ipp/print", "/ipp/port1"];
    private static readonly string[] ProbeAttributes = ["printer-state"];

    private readonly HttpClient _httpClient;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _resourcePath;
    private readonly IppTransportOptions _options;
    private Uri? _resolved;

    public IppEndpointResolver(HttpClient httpClient, string host, int port, string? resourcePath)
        : this(httpClient, host, port, resourcePath, new IppTransportOptions())
    {
    }

    public IppEndpointResolver(HttpClient httpClient, string host, int port, string? resourcePath, IppTransportOptions options)
    {
        _httpClient = httpClient;
        _host = host;
        _port = port;
        _resourcePath = resourcePath;
        _options = options;
    }

    // Concurrent first calls each probe. The first answer wins and the others are equal.
    public async Task<Uri> ResolveAsync(CancellationToken cancellationToken)
    {
        var resolved = Volatile.Read(ref _resolved);
        if (resolved is not null)
        {
            return resolved;
        }

        Exception? lastFailure = null;
        var schemes = _options.AllowPlainIpp ? ["ipps", "ipp"] : new[] { "ipps" };
        foreach (var scheme in schemes)
        {
            foreach (var path in GetPaths(_resourcePath))
            {
                var uri = new UriBuilder(scheme, _host, _port, path).Uri;
                var failure = await ProbeAsync(uri, cancellationToken).ConfigureAwait(false);
                if (failure is null)
                {
                    return Interlocked.CompareExchange(ref _resolved, uri, null) ?? uri;
                }

                lastFailure = failure;
            }
        }

        var over = _options.AllowPlainIpp ? "IPPS or IPP" : "IPPS";
        throw new InvalidOperationException($"Printer '{_host}:{_port}' did not answer IPP over {over}.", lastFailure);
    }

    // A transport failure clears the URI, so a printer that moved is found again.
    public async Task<T> RunAsync<T>(Func<Uri, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var uri = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or TimeoutException)
        {
            _ = Interlocked.CompareExchange(ref _resolved, null, uri);
            throw;
        }
    }

    // Returns null when the endpoint answered, the reason when it is not there.
    private async Task<Exception?> ProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(_httpClient, capture);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = ProbeAttributes },
        };

        try
        {
            _ = await client.GetPrinterAttributesAsync(request, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return new TimeoutException($"IPP probe of '{uri}' timed out.", exception);
        }
        catch (HttpRequestException exception) when (_options.StrictTls && IsTlsFailure(exception))
        {
            // A plain IPP retry would send the document in clear text to an untrusted host.
            throw new AuthenticationException($"The TLS handshake with '{uri}' failed, and plain IPP is not used for a validating client.", exception);
        }
        catch (HttpRequestException exception)
        {
            // A refused connection, an unknown host or a plain-IPP-only port: not there.
            if (exception.StatusCode is null || exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return exception;
            }

            throw new InvalidOperationException($"IPP query to '{uri}' failed with HTTP {exception.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // An HTTP error with an IPP body, for example 401: an answer, not a malformed one.
            throw new InvalidOperationException($"IPP query to '{uri}' failed with HTTP {http.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // No status was read, so the response is malformed, not an IPP error.
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
        catch (IppResponseException exception) when (IppFailureMapping.StatusCodeOf(capture.Response) == IppStatusCode.ClientErrorNotFound)
        {
            // CUPS answers an unknown path with HTTP 200 and this IPP status.
            return exception;
        }
        catch (IppResponseException exception)
        {
            throw IppFailureMapping.ToIppError(uri, exception);
        }
        catch (IppRequestException exception)
        {
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
    }

    // A handshake failure hides an AuthenticationException anywhere in the inner chain.
    private static bool IsTlsFailure(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    // A DNS-SD "rp" path has no leading slash and may repeat a well-known path.
    private static IReadOnlyList<string> GetPaths(string? resourcePath)
    {
        if (String.IsNullOrWhiteSpace(resourcePath))
        {
            return WellKnownPaths;
        }

        var trimmed = resourcePath.Trim();
        var normalized = trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
        List<string> paths = [normalized];
        paths.AddRange(WellKnownPaths.Where(path => !String.Equals(path, normalized, StringComparison.Ordinal)));
        return paths;
    }
}
