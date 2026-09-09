using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol.Models;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppJobMapperTests
{
    [Theory]
    [InlineData(JobState.Pending, PrintJobState.Queued)]
    [InlineData(JobState.PendingHeld, PrintJobState.Paused)]
    [InlineData(JobState.Processing, PrintJobState.Printing)]
    [InlineData(JobState.ProcessingStopped, PrintJobState.Paused)]
    [InlineData(JobState.Completed, PrintJobState.Completed)]
    [InlineData(JobState.Canceled, PrintJobState.Canceled)]
    [InlineData(JobState.Aborted, PrintJobState.Failed)]
    public void MapState_FollowsTheSpecTable(JobState ipp, PrintJobState expected) =>
        Assert.Equal(expected, IppJobStateMapper.Map(ipp));

    [Fact]
    public void MapState_ReportsQueuedForNoState() =>
        Assert.Equal(PrintJobState.Queued, IppJobStateMapper.Map(null));

    [Fact]
    public void Map_ReadsTheProgressCounters()
    {
        JobDescriptionAttributes attributes = new()
        {
            JobId = 42,
            JobName = "label.zpl",
            JobState = JobState.Processing,
            JobImpressions = 10,
            JobImpressionsCompleted = 4,
            JobStateReasons = [],
        };

        var job = IppJobMapper.Map(PrinterId.FromNetwork("printer.local"), attributes);

        Assert.Equal("42", job.JobId);
        Assert.Equal("label.zpl", job.JobName);
        Assert.Equal(PrintJobState.Printing, job.State);
        Assert.Equal(4, job.ImpressionsCompleted);
        Assert.Equal(10, job.TotalImpressions);
    }
}
