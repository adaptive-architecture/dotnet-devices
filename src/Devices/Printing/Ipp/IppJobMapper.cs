using System.Globalization;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP job-description attributes into the library job model.
internal static class IppJobMapper
{
    // Asked for explicitly. A printer left to its own default answers Get-Jobs with the job
    // identifier alone, and none of the messages that say why a job stopped.
    public static readonly string[] RequestedAttributes =
    [
        "job-id",
        "job-name",
        "job-state",
        "job-state-reasons",
        "job-state-message",
        "job-detailed-status-messages",
        "job-printer-state-message",
        "job-impressions",
        "job-impressions-completed",
        "date-time-at-creation",
        "date-time-at-completed",
    ];

    // "job-impressions" is integer(0:MAX), so a negative count is not one. A printer that
    // answers the attribute out of band — 'unknown' on a job it failed before the first
    // page — reaches this mapping as Int32.MinValue, which then reads as a page total.
    private static int? Impressions(int? value) => value >= 0 ? value : null;

    // Returns null for a job without job-id: it can be neither tracked nor cancelled.
    public static PrintJobInfo? Map(
        PrinterId id,
        JobDescriptionAttributes attributes,
        IIppResponseMessage? raw = null,
        int index = 0,
        IReadOnlyList<IppAttributeSnapshot>? rawAttributes = null)
    {
        if (attributes.JobId is not int numericId)
        {
            return null;
        }

        var reasons = StateReasons.Read(attributes.JobStateReasons);
        PrintJobInfo job = new(numericId.ToString(CultureInfo.InvariantCulture), id, IppJobStateMapper.Map(attributes.JobState))
        {
            JobName = attributes.JobName,
            ImpressionsCompleted = Impressions(attributes.JobImpressionsCompleted),
            TotalImpressions = Impressions(attributes.JobImpressions),
            Detail = StateReasons.Join(reasons),
            StateReasons = reasons,
            StateMessage = IppStatusMapper.Trim(attributes.JobStateMessage),

            // SharpIppNext does not model job-printer-state-message, which is where CUPS
            // puts the text of its own log. That text is what names a cause the state
            // reasons cannot say, so it is read from the raw answer.
            PrinterStateMessage = IppRawAttributes.ReadJobText(raw, index, "job-printer-state-message"),
            DetailedStatusMessages = IppStatusMapper.Messages(attributes.JobDetailedStatusMessages),
            RawAttributes = rawAttributes ?? [],
        };

        if (attributes.DateTimeAtCreation is DateTimeOffset created)
        {
            job.CreatedAt = created;
        }

        job.CompletedAt = attributes.DateTimeAtCompleted;

        return job;
    }
}
