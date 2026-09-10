namespace AdaptArch.Devices.Printing.Spooler;

// Turns the WINSPOOL status bit fields into the library state enums. P/Invoke-free on
// purpose, so every branch is testable on any platform.
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
    private const uint PrinterStatusNoToner = 0x00040000;
    private const uint PrinterStatusUserIntervention = 0x00100000;
    private const uint PrinterStatusDoorOpen = 0x00400000;

    // PRINTER_ATTRIBUTE_WORK_OFFLINE: with "Use Printer Offline" set the spooler holds
    // every job and reports no PRINTER_STATUS_OFFLINE bit, so this is the only sign.
    private const uint PrinterAttributeWorkOffline = 0x00000400;

    private const uint ErrorBits = PrinterStatusError | PrinterStatusPendingDeletion | PrinterStatusPaperJam | PrinterStatusPaperOut
        | PrinterStatusPaperProblem | PrinterStatusNoToner | PrinterStatusUserIntervention | PrinterStatusDoorOpen;

    private const uint OfflineBits = PrinterStatusOffline | PrinterStatusNotAvailable;

    // Several bits can be set at once, so the most serious one is checked first.
    internal static PrinterStatusState MapPrinterStatus(uint status, uint attributes)
    {
        if ((status & ErrorBits) != 0)
        {
            return PrinterStatusState.Error;
        }

        if ((status & OfflineBits) != 0 || IsWorkOffline(attributes))
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

    // Full Win32 macro names, so PrinterStatus.Detail compares against the Windows docs.
    internal static string? DescribePrinterStatus(uint status, uint attributes)
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
        AddIfSet(named, status, PrinterStatusNoToner, "PRINTER_STATUS_NO_TONER");
        AddIfSet(named, status, PrinterStatusUserIntervention, "PRINTER_STATUS_USER_INTERVENTION");
        AddIfSet(named, status, PrinterStatusDoorOpen, "PRINTER_STATUS_DOOR_OPEN");
        AddIfSet(named, attributes, PrinterAttributeWorkOffline, "PRINTER_ATTRIBUTE_WORK_OFFLINE");

        return named.Count == 0 ? null : String.Join("; ", named);
    }

    // The same bit groups MapPrinterStatus treats as Error, Offline or Paused.
    internal static bool IsAcceptingJobs(uint status, uint attributes) =>
        (status & (ErrorBits | OfflineBits | PrinterStatusPaused)) == 0 && !IsWorkOffline(attributes);

    private static bool IsWorkOffline(uint attributes) => (attributes & PrinterAttributeWorkOffline) != 0;

    private const uint JobStatusPaused = 0x00000001;
    private const uint JobStatusError = 0x00000002;
    private const uint JobStatusDeleting = 0x00000004;
    private const uint JobStatusPrinting = 0x00000010;
    private const uint JobStatusPrinted = 0x00000080;
    private const uint JobStatusDeleted = 0x00000100;
    private const uint JobStatusComplete = 0x00001000;

    // Checked most terminal first. Any unlisted bit means the job is still queued.
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
