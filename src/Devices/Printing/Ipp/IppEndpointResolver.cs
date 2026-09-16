using System.Linq;
using System.Security.Authentication;
using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// The IPP path differs between makers, so the probe tries the caller path, then the
// well-known paths, over TLS and then plain. The answer is kept until a transport failure.
internal sealed class IppEndpointResolver
{
    private const string ProbeOperation = "Get-Printer-Attributes";
    private static readonly string[] WellKnownPaths = ["/ipp/print", "/ipp/port1"];
    private static readonly string[] ProbeAttributes = ["printer-state"];

    private readonly IppContext _context;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _resourcePath;
    private Uri? _resolved;

    public IppEndpointResolver(IppContext context, string host, int port, string? resourcePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _host = host;
        _port = port;
        _resourcePath = resourcePath;
    }

    // The endpoint that answered, or null before one did. A transport failure clears it.
    public Uri? Resolved => Volatile.Read(ref _resolved);

    // Concurrent first calls each probe. The first answer wins and the others are equal.
    public async Task<Uri> ResolveAsync(CancellationToken cancellationToken)
    {
        var resolved = Volatile.Read(ref _resolved);
        if (resolved is not null)
        {
            return resolved;
        }

        // Every probe is kept. The first failure is usually the true cause: a TLS handshake
        // that failed over IPPS says more than the "connection refused" of a later plain port.
        Dictionary<Uri, Exception> failures = [];

        // Kept apart from the dictionary, whose order the BCL does not promise.
        Exception? firstFailure = null;
        var schemes = _context.Options.AllowPlainIpp ? ["ipps", "ipp"] : new[] { "ipps" };
        foreach (var scheme in schemes)
        {
            foreach (var path in GetPaths(_resourcePath))
            {
                var uri = new UriBuilder(scheme, _host, _port, path).Uri;
                var failure = await ProbeAsync(uri, cancellationToken).ConfigureAwait(false);
                if (failure is null)
                {
                    IppLog.EndpointProbed(_context.Logger, uri);
                    var winner = Interlocked.CompareExchange(ref _resolved, uri, null) ?? uri;
                    IppLog.EndpointResolved(_context.Logger, winner, _host, _port);
                    return winner;
                }

                IppLog.EndpointProbeFailed(_context.Logger, uri, failure);
                failures[uri] = failure;
                firstFailure ??= failure;
            }
        }

        var over = _context.Options.AllowPlainIpp ? "IPPS or IPP" : "IPPS";
        throw new PrinterConnectionException(_host, _port, over, failures, firstFailure!);
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
        IppCall call = new(_context, uri, ProbeOperation);
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(_context.Client, capture);
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
        catch (HttpRequestException exception) when (_context.Options.StrictTls && IsTlsFailure(exception))
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

            throw call.Failure($"IPP query to '{uri}' failed with HTTP {exception.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // An HTTP error with an IPP body, for example 401: an answer, not a malformed one.
            call.Response = capture.Response;
            throw call.Failure($"IPP query to '{uri}' failed with HTTP {http.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // No status was read, so the response is malformed, not an IPP error.
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
        catch (IppResponseException exception) when (IppFailureMapping.StatusCodeOf(capture.Response) == IppStatusCodes.ClientErrorNotFound)
        {
            // CUPS answers an unknown path with HTTP 200 and this IPP status.
            return exception;
        }
        catch (IppResponseException exception)
        {
            call.Response = capture.Response;
            throw IppFailureMapping.ToIppError(call, exception);
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
