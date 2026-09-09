namespace AdaptArch.Devices.Printing.Spooler;

// Turns the WINSPOOL status bit fields into the library's own state enums. Pure and
// P/Invoke-free on purpose, so every branch runs as a real test on any platform: the
// bit constants below are the only part that cannot be checked against a live Windows
// header from here, but they are the widely published PRINTER_STATUS_* and
// JOB_STATUS_* values (Microsoft Learn "Printer Status Values" / "Job Status Values"),
// unchanged since Windows 2000. IppJobStateMapper does not fit here: it maps IPP job
// state keywords, not WINSPOOL bit flags, so this is a separate, purpose-built mapping.
internal static class WindowsSpoolerStatusMapper
{
    private const uint PrinterStatusPaused = 0x00000001;
    private const uint PrinterStatusError = 0x00000002;
    private const uint PrinterStatusPendingDeletion = 0x00000004;
    private const uint PrinterStatusPaperJam = 0x00000008;
    private const uint PrinterStatusPaperOut = 0x00000010;
    private const uint PrinterStatusPaperProblem = 0x00000040;
    private const uint PrinterStatusOffline = 0x00000080;
    private const uint PrinterStatusPrinting = 0x00000400;
    private const uint PrinterStatusNotAvailable = 0x00001000;
    private const uint PrinterStatusProcessing = 0x00004000;
    private const uint PrinterStatusDoorOpen = 0x00400000;

    // A printer can report several bits at once (for example paused and offline).
    // Error conditions are checked first because they need the most attention, then
    // offline, then paused, then activity; zero, or any other unrecognised bit alone,
    // falls through to idle. PENDING_DELETION is grouped with the error bits: a queue
    // being torn down is not "ready" in any sense a caller should act on as idle.
    internal static PrinterStatusState MapPrinterStatus(uint status)
    {
        if ((status & (PrinterStatusError | PrinterStatusPendingDeletion | PrinterStatusPaperJam | PrinterStatusPaperOut | PrinterStatusPaperProblem | PrinterStatusDoorOpen)) != 0)
        {
            return PrinterStatusState.Error;
        }

        if ((status & (PrinterStatusOffline | PrinterStatusNotAvailable)) != 0)
        {
            return PrinterStatusState.Offline;
        }

        if ((status & PrinterStatusPaused) != 0)
        {
            return PrinterStatusState.Paused;
        }

        if ((status & (PrinterStatusPrinting | PrinterStatusProcessing)) != 0)
        {
            return PrinterStatusState.Processing;
        }

        return PrinterStatusState.Idle;
    }

    // Named for the same PRINTER_STATUS_* bits MapPrinterStatus reads, so
    // PrinterStatus.Detail carries the bits behind the mapped state instead of nothing,
    // the same way DescribeJobStatus below fills PrintJobInfo.Detail. Unlike
    // DescribeJobStatus, this spells out the full Win32 macro name, so a person can
    // compare the output directly against Windows documentation.
    internal static string? DescribePrinterStatus(uint status)
    {
        List<string> named = [];
        AddIfSet(named, status, PrinterStatusPaused, "PRINTER_STATUS_PAUSED");
        AddIfSet(named, status, PrinterStatusError, "PRINTER_STATUS_ERROR");
        AddIfSet(named, status, PrinterStatusPendingDeletion, "PRINTER_STATUS_PENDING_DELETION");
        AddIfSet(named, status, PrinterStatusPaperJam, "PRINTER_STATUS_PAPER_JAM");
        AddIfSet(named, status, PrinterStatusPaperOut, "PRINTER_STATUS_PAPER_OUT");
        AddIfSet(named, status, PrinterStatusPaperProblem, "PRINTER_STATUS_PAPER_PROBLEM");
        AddIfSet(named, status, PrinterStatusOffline, "PRINTER_STATUS_OFFLINE");
        AddIfSet(named, status, PrinterStatusPrinting, "PRINTER_STATUS_PRINTING");
        AddIfSet(named, status, PrinterStatusNotAvailable, "PRINTER_STATUS_NOT_AVAILABLE");
        AddIfSet(named, status, PrinterStatusProcessing, "PRINTER_STATUS_PROCESSING");
        AddIfSet(named, status, PrinterStatusDoorOpen, "PRINTER_STATUS_DOOR_OPEN");

        return named.Count == 0 ? null : String.Join("; ", named);
    }

    // Mirrors the bit groups MapPrinterStatus treats as Error, Offline or Paused: a
    // queue reporting any of them does not take a new job right now, the same way the
    // IPP path reports PrinterIsAcceptingJobs as false for those states.
    internal static bool IsAcceptingJobs(uint status) =>
        (status & (PrinterStatusError | PrinterStatusPendingDeletion | PrinterStatusPaperJam | PrinterStatusPaperOut
            | PrinterStatusPaperProblem | PrinterStatusDoorOpen | PrinterStatusOffline | PrinterStatusNotAvailable
            | PrinterStatusPaused)) == 0;

    private const uint JobStatusPaused = 0x00000001;
    private const uint JobStatusError = 0x00000002;
    private const uint JobStatusDeleting = 0x00000004;
    private const uint JobStatusPrinting = 0x00000010;
    private const uint JobStatusPrinted = 0x00000080;
    private const uint JobStatusDeleted = 0x00000100;
    private const uint JobStatusComplete = 0x00001000;

    // Same reasoning as above: deletion is the most terminal outcome so it is checked
    // first, then error, then paused, then the two "done" bits, then active printing;
    // spooling, blocked, restarted, retained or no bit at all all mean "still queued".
    internal static PrintJobState MapJobStatus(uint status)
    {
        if ((status & (JobStatusDeleted | JobStatusDeleting)) != 0)
        {
            return PrintJobState.Canceled;
        }

        if ((status & JobStatusError) != 0)
        {
            return PrintJobState.Failed;
        }

        if ((status & JobStatusPaused) != 0)
        {
            return PrintJobState.Paused;
        }

        if ((status & (JobStatusPrinted | JobStatusComplete)) != 0)
        {
            return PrintJobState.Completed;
        }

        if ((status & JobStatusPrinting) != 0)
        {
            return PrintJobState.Printing;
        }

        return PrintJobState.Queued;
    }

    // Named for the same JOB_STATUS_* bits MapJobStatus reads, so PrintJobInfo.Detail
    // carries the bits behind the mapped state instead of nothing, the way the IPP
    // path joins job-state-reasons for the same field.
    internal static string? DescribeJobStatus(uint status)
    {
        List<string> named = [];
        AddIfSet(named, status, JobStatusPaused, "Paused");
        AddIfSet(named, status, JobStatusError, "Error");
        AddIfSet(named, status, JobStatusDeleting, "Deleting");
        AddIfSet(named, status, JobStatusPrinting, "Printing");
        AddIfSet(named, status, JobStatusPrinted, "Printed");
        AddIfSet(named, status, JobStatusDeleted, "Deleted");
        AddIfSet(named, status, JobStatusComplete, "Complete");

        return named.Count == 0 ? null : String.Join("; ", named);
    }

    private static void AddIfSet(List<string> named, uint status, uint flag, string name)
    {
        if ((status & flag) != 0)
        {
            named.Add(name);
        }
    }
}
