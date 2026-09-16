using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Sends one IPP operation and gives every failure the same shape. Each call gets a fresh
// protocol and client, so the captured raw response belongs to that call only.
internal sealed class IppOperations
{
    private readonly IppCall _call;

    public IppOperations(IppContext context, Uri endpoint, string operation)
        : this(context, endpoint, operation, null)
    {
    }

    public IppOperations(IppContext context, Uri endpoint, string operation, PrinterId? printerId) =>
        _call = new IppCall(context, endpoint, operation, printerId);

    public IppCall Call => _call;

    public IIppResponseMessage? LastRawResponse => _call.Response;

    public IReadOnlyList<IppAttributeSnapshot> RawAttributes => _call.RawAttributes;

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        Func<SharpIppClient, TRequest, CancellationToken, Task<TResponse>> operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var uri = _call.Endpoint;
        var logger = _call.Context.Logger;
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(_call.Context.Client, capture);
        try
        {
            var response = await operation(client, request, cancellationToken).ConfigureAwait(false);
            _call.Response = capture.Response;
            IppLog.OperationCompleted(logger, _call.Operation, uri, _call.StatusCode);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw Fail(new TimeoutException($"IPP request to '{uri}' timed out.", exception));
        }
        catch (HttpRequestException exception)
        {
            // A null status code means no HTTP response at all: name that case.
            throw Fail(IppFailureMapping.ToHttpError(_call, exception.StatusCode, exception));
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // An HTTP error with an IPP body, for example 401: an answer, not a malformed one.
            _call.Response = capture.Response;
            throw Fail(IppFailureMapping.ToHttpError(_call, http.StatusCode, exception));
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // No status was read, so the response is malformed, not an IPP error.
            throw Fail(IppFailureMapping.ToMalformedResponse(uri, exception));
        }
        catch (IppResponseException exception)
        {
            // The response parsed, so keep it: a caller may need the IPP status code.
            _call.Response = capture.Response;
            throw Fail(IppFailureMapping.ToIppError(_call, exception));
        }
        catch (IppRequestException exception)
        {
            throw Fail(IppFailureMapping.ToMalformedResponse(uri, exception));
        }
    }

    private T Fail<T>(T exception)
        where T : Exception
    {
        IppLog.OperationFailed(_call.Context.Logger, _call.Operation, _call.Endpoint, _call.StatusCode, exception);
        return exception;
    }
}
