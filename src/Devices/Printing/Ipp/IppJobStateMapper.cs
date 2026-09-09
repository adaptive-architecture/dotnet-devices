using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns an IPP job state into the library job state. Shared so IppPrinter and the job
// queue (a later addition) map the same wire values the same way.
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

        // No job-state attribute at all: the job was just accepted, so it is queued.
        return PrintJobState.Queued;
    }
}
