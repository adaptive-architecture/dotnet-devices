using System.Globalization;

namespace AdaptArch.Devices.Printing;

// One job, as two channels would both describe it if they read one queue.
//
// The type exists to answer a single question: is this job distinctive enough that two
// channels reporting it are reporting the same queue, and not two printers that happen to
// look alike? Most jobs are not. A queue that holds only jobs that are not is no evidence
// at all, and the correlation then says nothing rather than guessing.
internal readonly record struct PrinterQueueFingerprint(
    int JobId,
    string? JobName,
    int? UptimeAtCreation,
    DateTimeOffset? CreatedAt,
    string? OriginatingUser)
{
    // A name every second job carries. It names the job no better than a blank name does,
    // and two printers in one office will both hold a job called "Document".
    private static readonly string[] PlaceholderNames =
    [
        "untitled", "document", "print job", "printjob", "test page", "testpage",
        "stdin", "(stdin)", "-", "untitled document", "no name", "unnamed",
    ];

    /// <summary>
    /// Gets a value indicating whether this job is distinctive enough to be evidence.
    /// </summary>
    /// <remarks>
    /// Three things must hold: the printer named the job, the name is not one every second
    /// job carries, and the printer said when the job was created. A job that fails any of
    /// them is dropped before the comparison, so a queue of such jobs compares as empty.
    /// </remarks>
    public bool IsDiscriminating =>
        (UptimeAtCreation is not null || CreatedAt is not null)
        && PrinterDeviceKey.IsUsableIdentity(JobName)
        && !PlaceholderNames.Contains(JobName!.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The text two channels must both produce for this job. It is compared, never parsed.
    /// </summary>
    public string Canonical => String.Create(
        CultureInfo.InvariantCulture,
        $"{JobId}|{JobName}|{UptimeAtCreation}|{CreatedAt?.ToUnixTimeSeconds()}|{OriginatingUser}");

    /// <summary>
    /// The text of a whole queue, in an order neither printer chose.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when the queue holds no discriminating job. That is "no answer"
    /// and never "no jobs in common": an empty queue matches nothing, not even another
    /// empty one, because two idle printers are still two printers.
    /// </remarks>
    public static string? Summarize(IEnumerable<PrinterQueueFingerprint> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        List<string> lines = [.. queue.Where(static job => job.IsDiscriminating).Select(static job => job.Canonical)];
        if (lines.Count == 0)
        {
            return null;
        }

        // Sorted, because a printer may list its queue in any order, and two channels of
        // one queue must still produce one text.
        lines.Sort(StringComparer.Ordinal);
        return String.Join("\n", lines);
    }
}
