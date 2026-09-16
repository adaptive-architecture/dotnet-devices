using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// One IPP request, with what a log entry and a failure both need: which operation, which
// endpoint, which printer, and the answer that came back. One instance serves one request.
internal sealed class IppCall
{
    public IppCall(IppContext context, Uri endpoint, string operation)
        : this(context, endpoint, operation, null)
    {
    }

    public IppCall(IppContext context, Uri endpoint, string operation, PrinterId? printerId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(endpoint);
        Context = context;
        Endpoint = endpoint;
        Operation = operation;
        PrinterId = printerId;
    }

    public IppContext Context { get; }

    public Uri Endpoint { get; }

    public string Operation { get; }

    public PrinterId? PrinterId { get; }

    // The answer, once the reader has read one. Null for a transport failure.
    public IIppResponseMessage? Response { get; set; }

    public int? StatusCode => IppFailureMapping.StatusCodeOf(Response);

    public IReadOnlyList<IppAttributeSnapshot> RawAttributes =>
        IppRawSnapshot.Project(Response, Context.Options.CaptureRawResponses);

    public PrinterOperationException Failure(string message, Exception innerException) =>
        new(message, innerException)
        {
            PrinterId = PrinterId,
            Endpoint = Endpoint,
            Operation = Operation,
            IppStatusCode = StatusCode,
            RawAttributes = RawAttributes,
        };
}
