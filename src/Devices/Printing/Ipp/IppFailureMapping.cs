using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns an IPP protocol failure into the exception the public API throws.
// SharpIppNext reports both a malformed response and a printer-reported error status as
// IppResponseException, and only an inner exception tells them apart, so the caller must
// pick the mapping with a `when (exception.InnerException is not null)` guard.
internal static class IppFailureMapping
{
    public static PrinterOperationException ToIppError(IppCall call, Exception exception) =>
        call.Failure($"Printer '{call.Endpoint}' reported an IPP error.", exception);

    public static PrinterOperationException ToHttpError(IppCall call, System.Net.HttpStatusCode? status, Exception exception) =>
        call.Failure(
            status is System.Net.HttpStatusCode code
                ? $"IPP request to '{call.Endpoint}' failed with HTTP {code:d}."
                : $"IPP request to '{call.Endpoint}' failed: no HTTP response was received. Confirm the printer or IPP daemon is reachable and listening.",
            exception);

    public static PrinterOperationException ToUnsupportedDocumentFormat(IppCall call, string documentFormat, Exception exception) =>
        call.Failure($"Printer '{call.Endpoint}' does not accept the document format '{documentFormat}'.", exception);

    public static InvalidDataException ToMalformedResponse(Uri uri, Exception exception) =>
        new($"The IPP response from '{uri}' is malformed.", exception);

    // Only the concrete IppResponseMessage exposes the status code.
    public static int? StatusCodeOf(IIppResponseMessage? response) =>
        response is IppResponseMessage typed ? (int)typed.StatusCode : null;
}
