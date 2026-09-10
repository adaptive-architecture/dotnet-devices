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
        "printer-is-accepting-jobs",
        "printer-make-and-model",
        "printer-location",
        "marker-names",
        "marker-colors",
        "marker-levels",
    ];

    public static IppPrinterDetails Map(PrinterId id, PrinterDescriptionAttributes? attributes, IIppResponseMessage? raw)
    {
        var state = MapState(attributes?.PrinterState, attributes?.PrinterStateReasons);
        var detail = JoinReasons(attributes?.PrinterStateReasons);
        var accepting = attributes?.PrinterIsAcceptingJobs ?? state is not (PrinterStatusState.Paused or PrinterStatusState.Error);

        PrinterStatus status = new(id, state)
        {
            IsAcceptingJobs = accepting,
            Detail = detail,
            Markers = IppMarkers.Read(raw),
        };
        PrinterInfo info = new(id, attributes?.PrinterMakeAndModel ?? attributes?.PrinterName ?? id.Authority)
        {
            Location = attributes?.PrinterLocation,
        };
        return new IppPrinterDetails(info, status);
    }

    private static bool HasErrorReason(PrinterStateReason[]? reasons) =>
        reasons is not null && Array.Exists(reasons, static reason => reason.ToString().EndsWith("-error", StringComparison.OrdinalIgnoreCase));

    private static string? JoinReasons(PrinterStateReason[]? reasons)
    {
        if (reasons is null || reasons.Length == 0)
        {
            return null;
        }

        List<string> named = [];
        foreach (var reason in reasons)
        {
            var text = reason.ToString();
            if (!String.IsNullOrWhiteSpace(text) && !String.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
            {
                named.Add(text);
            }
        }

        return named.Count == 0 ? null : String.Join("; ", named);
    }

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
        // (RFC 8011 §5.4.12) says the halt is a fault, not a pause.
        if (state == PrinterState.Stopped)
        {
            return HasErrorReason(reasons) ? PrinterStatusState.Error : PrinterStatusState.Paused;
        }

        return PrinterStatusState.Unknown;
    }
}
