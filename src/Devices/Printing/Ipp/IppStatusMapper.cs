using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP printer attributes into the library status model. Shared by
// IppPrinterStatusClient and IppPrinter, so the state map, the state-reason join and the
// marker read live in one place.
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
        var state = MapState(attributes?.PrinterState);
        var detail = JoinReasons(attributes?.PrinterStateReasons);
        var accepting = attributes?.PrinterIsAcceptingJobs ?? state != PrinterStatusState.Paused;

        PrinterStatus status = new(id, state)
        {
            IsAcceptingJobs = accepting,
            Detail = detail,
            Markers = IppMarkers.Read(raw),
        };
        PrinterInfo info = new(id, attributes?.PrinterMakeAndModel ?? attributes?.PrinterName ?? id.Value)
        {
            Location = attributes?.PrinterLocation,
        };
        return new IppPrinterDetails(info, status);
    }

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

    private static PrinterStatusState MapState(PrinterState? state)
    {
        if (state == PrinterState.Idle)
        {
            return PrinterStatusState.Idle;
        }

        if (state == PrinterState.Processing)
        {
            return PrinterStatusState.Processing;
        }

        // IPP reports a printer that has halted as "stopped".
        if (state == PrinterState.Stopped)
        {
            return PrinterStatusState.Paused;
        }

        return PrinterStatusState.Unknown;
    }
}
