using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns an IPP protocol failure into the exception type the public API throws. Shared by
// IppEndpointResolver and IppOperations, so the two catch chains do not repeat this mapping.
// A refused or absent endpoint is not a protocol failure, and each caller handles it on its own.
//
// SharpIppNext reports a response it could not parse as an IppResponseException with an
// inner exception (for example EndOfStreamException on a truncated payload), and a response
// that parsed but carries an IPP error status as an IppResponseException with no inner
// exception. The two need different exception types, so callers must tell them apart before
// asking for the mapping, typically with a `when (exception.InnerException is not null)` guard.
//
// ToIppError throws a plain InvalidOperationException, never a derived type: the tests
// assert this exact type, and xunit matches an exception type exactly. A caller that needs
// the IPP status code reads it with StatusCodeOf(IppOperations.LastRawResponse) instead.
internal static class IppFailureMapping
{
    public static InvalidOperationException ToIppError(Uri uri, Exception exception) =>
        new($"Printer '{uri}' reported an IPP error.", exception);

    public static InvalidDataException ToMalformedResponse(Uri uri, Exception exception) =>
        new($"The IPP response from '{uri}' is malformed.", exception);

    // IIppResponseMessage itself does not expose the status code; only the concrete type
    // SharpIppNext actually returns, IppResponseMessage, does.
    public static IppStatusCode? StatusCodeOf(IIppResponseMessage? response) =>
        response is IppResponseMessage typed ? typed.StatusCode : null;
}
