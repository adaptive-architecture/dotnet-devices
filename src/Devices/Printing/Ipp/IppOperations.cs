using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Sends one IPP operation and gives every failure the same shape. Each call gets a fresh
// protocol and client, so the captured raw response belongs to that call only.
internal sealed class IppOperations
{
    private readonly HttpClient _httpClient;

    public IppOperations(HttpClient httpClient) => _httpClient = httpClient;

    public IIppResponseMessage? LastRawResponse { get; private set; }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        Func<SharpIppClient, TRequest, CancellationToken, Task<TResponse>> operation,
        TRequest request,
        Uri uri,
        CancellationToken cancellationToken)
    {
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(_httpClient, capture);
        try
        {
            var response = await operation(client, request, cancellationToken).ConfigureAwait(false);
            LastRawResponse = capture.Response;
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"IPP request to '{uri}' timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            // A null status code means no HTTP response at all: name that case.
            var message = exception.StatusCode is System.Net.HttpStatusCode status
                ? $"IPP request to '{uri}' failed with HTTP {status:d}."
                : $"IPP request to '{uri}' failed: no HTTP response was received. Confirm the printer or IPP daemon is reachable and listening.";
            throw new InvalidOperationException(message, exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // An HTTP error with an IPP body, for example 401: an answer, not a malformed one.
            LastRawResponse = capture.Response;
            throw new InvalidOperationException($"IPP request to '{uri}' failed with HTTP {http.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // No status was read, so the response is malformed, not an IPP error.
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
        catch (IppResponseException exception)
        {
            // The response parsed, so keep it: a caller may need the IPP status code.
            LastRawResponse = capture.Response;
            throw IppFailureMapping.ToIppError(uri, exception);
        }
        catch (IppRequestException exception)
        {
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
    }
}
