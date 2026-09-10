using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Sends one IPP operation and gives every failure the same shape. A fresh protocol and a
// fresh client belong to each call, so the captured raw response belongs to that call only.
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
            // The caller did not cancel, so the HttpClient timeout fired.
            throw new TimeoutException($"IPP request to '{uri}' timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            // A null status code means the request never got an HTTP response at all,
            // most often because nothing is listening (on Linux, no CUPS daemon at
            // localhost:631). Name that case instead of formatting it as nothing.
            var message = exception.StatusCode is System.Net.HttpStatusCode status
                ? $"IPP request to '{uri}' failed with HTTP {status:d}."
                : $"IPP request to '{uri}' failed: no HTTP response was received. Confirm the printer or IPP daemon is reachable and listening.";
            throw new InvalidOperationException(message, exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // The printer answered with an HTTP error and an IPP body, for example 401 when
            // it needs authentication. That is an answer, not a malformed response.
            LastRawResponse = capture.Response;
            throw new InvalidOperationException($"IPP request to '{uri}' failed with HTTP {http.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // The protocol reader failed before it could read a status, so this response
            // is malformed rather than a printer-reported IPP error.
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
        catch (IppResponseException exception)
        {
            // The response parsed fully before SharpIppNext decided its status code was an
            // error and threw, so the capture already holds it. Keep it on LastRawResponse
            // even though this call is about to throw, so a caller that needs the status
            // code (see IppFailureMapping.StatusCodeOf) can still read it.
            LastRawResponse = capture.Response;
            throw IppFailureMapping.ToIppError(uri, exception);
        }
        catch (IppRequestException exception)
        {
            throw IppFailureMapping.ToMalformedResponse(uri, exception);
        }
    }
}
