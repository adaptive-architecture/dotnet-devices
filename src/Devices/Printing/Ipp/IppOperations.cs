using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Sends one IPP operation and gives every failure the same shape. Each call gets a fresh
// protocol and client, so the captured raw response belongs to that call only.
internal sealed class IppOperations
{
    public IppOperations(IppContext context, Uri endpoint, string operation)
        : this(context, endpoint, operation, null)
    {
    }

    public IppOperations(IppContext context, Uri endpoint, string operation, PrinterId? printerId) =>
        Call = new IppCall(context, endpoint, operation, printerId);

    public IppCall Call { get; }

    public IIppResponseMessage? LastRawResponse => Call.Response;

    public IReadOnlyList<IppAttributeSnapshot> RawAttributes => Call.RawAttributes;

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        Func<SharpIppClient, TRequest, CancellationToken, Task<TResponse>> operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var uri = Call.Endpoint;
        var logger = Call.Context.Logger;
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(Call.Context.Client, capture);
        try
        {
            var response = await operation(client, request, cancellationToken).ConfigureAwait(false);
            Call.Response = capture.Response;
            IppLog.OperationCompleted(logger, Call.Operation, uri, Call.StatusCode);
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
            throw Fail(IppFailureMapping.ToHttpError(Call, exception.StatusCode, exception));
        }
        catch (IppResponseException exception) when (exception.InnerException is HttpRequestException http)
        {
            // An HTTP error with an IPP body, for example 401: an answer, not a malformed one.
            Call.Response = capture.Response;
            throw Fail(IppFailureMapping.ToHttpError(Call, http.StatusCode, exception));
        }
        catch (IppResponseException exception) when (exception.InnerException is not null)
        {
            // No status was read, so the response is malformed, not an IPP error.
            throw Fail(IppFailureMapping.ToMalformedResponse(uri, exception));
        }
        catch (IppResponseException exception)
        {
            // The response parsed, so keep it: a caller may need the IPP status code.
            Call.Response = capture.Response;
            throw Fail(IppFailureMapping.ToIppError(Call, exception));
        }
        catch (IppRequestException exception)
        {
            throw Fail(IppFailureMapping.ToMalformedResponse(uri, exception));
        }
    }

    private T Fail<T>(T exception)
        where T : Exception
    {
        IppLog.OperationFailed(Call.Context.Logger, Call.Operation, Call.Endpoint, Call.StatusCode, exception);
        return exception;
    }
}
