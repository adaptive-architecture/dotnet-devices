using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// What one Print-Job operation sends, apart from the printer it goes to.
internal sealed record IppSubmission(
    PrinterPayload Payload,
    string DocumentFormat,
    PrintOptions? Options,
    IReadOnlyList<DroppedOption> Dropped)
{
    // Job attributes no PrintOptions property maps to, appended as they are. Only a CUPS
    // queue is sent any: a server extension such as "fit-to-page" means nothing to a printer.
    public IReadOnlyList<IppAttribute> ExtraJobAttributes { get; init; } = [];
}
