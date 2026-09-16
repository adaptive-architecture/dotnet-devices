using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP printer attributes into the library status model.
internal static class IppStatusMapper
{
    public static readonly string[] RequestedAttributes =
    [
        "printer-state",
        "printer-state-reasons",
        "printer-state-message",
        "printer-detailed-status-messages",
        "printer-is-accepting-jobs",
        "printer-make-and-model",
        "printer-location",
        "marker-names",
        "marker-colors",
        "marker-levels",
    ];

    public static IppPrinterDetails Map(
        PrinterId id,
        PrinterDescriptionAttributes? attributes,
        IIppResponseMessage? raw,
        PrinterConnection? connection = null,
        IReadOnlyList<IppAttributeSnapshot>? rawAttributes = null)
    {
        var state = MapState(attributes?.PrinterState, attributes?.PrinterStateReasons);
        var reasons = StateReasons.Read(attributes?.PrinterStateReasons);
        var accepting = attributes?.PrinterIsAcceptingJobs ?? state is not (PrinterStatusState.Paused or PrinterStatusState.Error);

        PrinterStatus status = new(id, state)
        {
            IsAcceptingJobs = accepting,
            Detail = StateReasons.Join(reasons),
            StateReasons = reasons,
            StateMessage = Trim(attributes?.PrinterStateMessage),
            DetailedStatusMessages = Messages(attributes?.PrinterDetailedStatusMessages),
            Connection = connection,
            RawAttributes = rawAttributes ?? [],
            Markers = IppMarkers.Read(raw),
        };
        PrinterInfo info = new(id, attributes?.PrinterMakeAndModel ?? attributes?.PrinterName ?? id.Authority)
        {
            Location = attributes?.PrinterLocation,
        };
        return new IppPrinterDetails(info, status);
    }

    // A message a printer never set comes back as an empty string, which says nothing.
    internal static string? Trim(string? message) => String.IsNullOrWhiteSpace(message) ? null : message.Trim();

    internal static IReadOnlyList<string> Messages(string[]? messages)
    {
        if (messages is null || messages.Length == 0)
        {
            return [];
        }

        List<string> named = [];
        foreach (var message in messages)
        {
            if (Trim(message) is string text)
            {
                named.Add(text);
            }
        }

        return named;
    }

    private static bool HasErrorReason(PrinterStateReason[]? reasons) =>
        reasons is not null && Array.Exists(reasons, static reason => reason.ToString().EndsWith("-error", StringComparison.OrdinalIgnoreCase));

    private static PrinterStatusState MapState(PrinterState? state, PrinterStateReason[]? reasons)
    {
        if (state == PrinterState.Idle)
        {
            return PrinterStatusState.Idle;
        }

        if (state == PrinterState.Processing)
        {
            return PrinterStatusState.Processing;
        }

        // IPP reports a halted printer as "stopped". An "-error" reason suffix
        // (RFC 8011 §5.4.12) says the halt is a fault, not a pause. The unfiltered list is
        // read on purpose: the state rule must not change with the reason filter.
        if (state == PrinterState.Stopped)
        {
            return HasErrorReason(reasons) ? PrinterStatusState.Error : PrinterStatusState.Paused;
        }

        return PrinterStatusState.Unknown;
    }
}
