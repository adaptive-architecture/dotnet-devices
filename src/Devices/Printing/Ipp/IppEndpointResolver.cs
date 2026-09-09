using System.Linq;
using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Finds the printer URI one time. A printer serves IPP at a path that differs between
// makers, so the probe tries the caller path first, then the two well-known paths, over
// TLS and then plain. The answer is kept, because every later operation needs the same URI.
internal sealed class IppEndpointResolver
{
    private static readonly string[] Schemes = ["ipps", "ipp"];
    private static readonly string[] WellKnownPaths = ["/ipp/print", "/ipp/port1"];
    private static readonly string[] ProbeAttributes = ["printer-state"];

    private readonly HttpClient _httpClient;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _resourcePath;
    private Uri? _resolved;

    public IppEndpointResolver(HttpClient httpClient, string host, int port, string? resourcePath)
    {
        _httpClient = httpClient;
        _host = host;
        _port = port;
        _resourcePath = resourcePath;
    }

    public async Task<Uri> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        foreach (var scheme in Schemes)
        {
            foreach (var path in GetPaths(_resourcePath))
            {
                Uri uri = new($"{scheme}://{_host}:{_port}{path}");
                if (await AnswersAsync(uri, cancellationToken).ConfigureAwait(false))
                {
                    _resolved = uri;
                    return uri;
                }
            }
        }

        throw new InvalidOperationException($"Printer '{_host}:{_port}' did not answer IPP over IPPS or IPP.");
    }

    private async Task<bool> AnswersAsync(Uri uri, CancellationToken cancellationToken)
    {
        using SharpIppClient client = new(_httpClient, new IppProtocol());
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = ProbeAttributes },
        };

        try
        {
            _ = await client.GetPrinterAttributesAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (HttpRequestException exception)
        {
            // A refused connection or an unknown host means this endpoint is not there.
            if (exception.StatusCode is null || exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            throw new InvalidOperationException($"IPP query to '{uri}' failed with HTTP {exception.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // The protocol reader failed before it could read a status, so this response
            // is malformed rather than a printer-reported IPP error.
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
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

    // A path from a DNS-SD "rp" attribute has no leading slash, and it may repeat one of
    // the well-known paths, so it is normalized and then de-duplicated.
    private static IReadOnlyList<string> GetPaths(string? resourcePath)
    {
        if (String.IsNullOrWhiteSpace(resourcePath))
        {
            return WellKnownPaths;
        }

        var trimmed = resourcePath.Trim();
        var normalized = trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
        List<string> paths = [normalized];
        foreach (var path in WellKnownPaths.Where(path => !String.Equals(path, normalized, StringComparison.Ordinal)))
        {
            paths.Add(path);
        }

        return paths;
    }
}
