using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

internal static class IppJobStateMapper
{
    public static PrintJobState Map(JobState? state)
    {
        if (state == JobState.Pending)
        {
            return PrintJobState.Queued;
        }

        if (state == JobState.PendingHeld)
        {
            return PrintJobState.Paused;
        }

        if (state == JobState.Processing)
        {
            return PrintJobState.Printing;
        }

        if (state == JobState.ProcessingStopped)
        {
            return PrintJobState.Paused;
        }

        if (state == JobState.Completed)
        {
            return PrintJobState.Completed;
        }

        if (state == JobState.Canceled)
        {
            return PrintJobState.Canceled;
        }

        if (state == JobState.Aborted)
        {
            return PrintJobState.Failed;
        }

        // No job-state attribute: the job was just accepted.
        return PrintJobState.Queued;
    }
}
