using System.Globalization;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP job-description attributes into the library job model. The job-state map is
// IppJobStateMapper, shared with IppPrinter, so this type only joins the reasons and
// copies the counters and timestamps.
internal static class IppJobMapper
{
    // Returns null for a job without job-id. Such an entry cannot be tracked or cancelled,
    // so the caller skips it instead of failing the whole list.
    public static PrintJobInfo? Map(PrinterId id, JobDescriptionAttributes attributes)
    {
        if (attributes.JobId is not int numericId)
        {
            return null;
        }

        PrintJobInfo job = new(numericId.ToString(CultureInfo.InvariantCulture), id, IppJobStateMapper.Map(attributes.JobState))
        {
            JobName = attributes.JobName,
            ImpressionsCompleted = attributes.JobImpressionsCompleted,
            TotalImpressions = attributes.JobImpressions,
            Detail = JoinReasons(attributes.JobStateReasons),
        };

        if (attributes.DateTimeAtCreation is DateTimeOffset created)
        {
            job.CreatedAt = created;
        }

        job.CompletedAt = attributes.DateTimeAtCompleted;

        return job;
    }

    private static string? JoinReasons(JobStateReason[]? reasons)
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
}
