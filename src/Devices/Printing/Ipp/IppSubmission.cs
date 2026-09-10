namespace AdaptArch.Devices.Printing.Ipp;

// What one Print-Job operation sends, apart from the printer it goes to.
internal sealed record IppSubmission(
    PrinterPayload Payload,
    string DocumentFormat,
    PrintOptions? Options,
    IReadOnlyList<string> Dropped);
