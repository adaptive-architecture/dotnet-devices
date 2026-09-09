using System.Globalization;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP job-description attributes into the library job model. The job-state map is
// IppJobStateMapper, shared with IppPrinter, so this type only joins the reasons and
// copies the counters and timestamps.
internal static class IppJobMapper
{
    public static PrintJobInfo Map(PrinterId id, JobDescriptionAttributes attributes)
    {
        var jobId = attributes.JobId?.ToString(CultureInfo.InvariantCulture) ?? String.Empty;
        PrintJobInfo job = new(jobId, id, IppJobStateMapper.Map(attributes.JobState))
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
