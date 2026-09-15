using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns IPP job-description attributes into the fingerprint the correlation compares.
//
// It is not IppJobMapper. That type builds the PrintJobInfo a caller watches, and reports
// progress and state reasons; this one keeps only what tells one job from another, and
// keeps both creation times, because a printer answers with one or the other.
internal static class IppQueueFingerprintMapper
{
    /// <summary>
    /// The attributes a fingerprint is made of, and nothing else.
    /// </summary>
    public static readonly string[] RequestedAttributes =
    [
        "job-id",
        "job-name",
        "job-state",
        "time-at-creation",
        "date-time-at-creation",
        "job-originating-user-name",
    ];

    /// <summary>
    /// Maps one job. Returns <c>null</c> for a job without <c>job-id</c>: it names nothing.
    /// </summary>
    public static PrinterQueueFingerprint? Map(JobDescriptionAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.JobId is not int id
            ? null
            : new PrinterQueueFingerprint(
                id,
                attributes.JobName,
                attributes.TimeAtCreation,
                attributes.DateTimeAtCreation,
                attributes.JobOriginatingUserName);
    }
}
