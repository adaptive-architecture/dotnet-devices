using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns an IPP protocol failure into the exception the public API throws.
// SharpIppNext reports both a malformed response and a printer-reported error status as
// IppResponseException, and only an inner exception tells them apart, so the caller must
// pick the mapping with a `when (exception.InnerException is not null)` guard.
internal static class IppFailureMapping
{
    public static InvalidOperationException ToIppError(Uri uri, Exception exception) =>
        new($"Printer '{uri}' reported an IPP error.", exception);

    public static InvalidOperationException ToUnsupportedDocumentFormat(Uri uri, string documentFormat, Exception exception) =>
        new($"Printer '{uri}' does not accept the document format '{documentFormat}'.", exception);

    public static InvalidDataException ToMalformedResponse(Uri uri, Exception exception) =>
        new($"The IPP response from '{uri}' is malformed.", exception);

    // Only the concrete IppResponseMessage exposes the status code.
    public static IppStatusCode? StatusCodeOf(IIppResponseMessage? response) =>
        response is IppResponseMessage typed ? typed.StatusCode : null;
}
