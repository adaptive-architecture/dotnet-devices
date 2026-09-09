using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintingModelTests
{
    [Fact]
    public void PrintOptions_DefaultToNull()
    {
        PrintOptions options = new();

        Assert.Null(options.Copies);
        Assert.Null(options.Duplex);
        Assert.Null(options.ColorMode);
        Assert.Null(options.Orientation);
        Assert.Null(options.MediaSource);
        Assert.Null(options.MediaSize);
        Assert.Null(options.ResolutionDpi);
        Assert.Null(options.JobName);
    }

    [Fact]
    public void PrinterStatus_DefaultsToAcceptingJobs()
    {
        var id = PrinterId.FromNetwork("host");
        PrinterStatus status = new(id, PrinterStatusState.Idle);

        Assert.Equal(id, status.PrinterId);
        Assert.Equal(PrinterStatusState.Idle, status.State);
        Assert.True(status.IsAcceptingJobs);
        Assert.Null(status.Detail);
        Assert.True(status.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void PrinterConfiguration_DefaultsToEmptyCapabilities()
    {
        var id = PrinterId.FromSpooler("Q");
        PrinterConfiguration configuration = new(id);

        Assert.Equal(id, configuration.PrinterId);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Empty(configuration.MediaSizes);
        Assert.False(configuration.SupportsDuplex);
        Assert.False(configuration.SupportsColor);
        Assert.Null(configuration.DefaultMediaSize);
    }

    [Fact]
    public void PrintJobInfo_RecordsCreationTime()
    {
        var id = PrinterId.FromUsb("dev");
        PrintJobInfo job = new("42", id, PrintJobState.Queued) { JobName = "Label" };

        Assert.Equal("42", job.JobId);
        Assert.Equal(id, job.PrinterId);
        Assert.Equal(PrintJobState.Queued, job.State);
        Assert.Equal("Label", job.JobName);
        Assert.Null(job.CompletedAt);
        Assert.True(job.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void DiscoveredPrinter_RequiresEndpointAndInfo()
    {
        var id = PrinterId.FromNetwork("host");
        Assert.Throws<ArgumentNullException>(() => new DiscoveredPrinter(id, null, new PrinterInfo(id, "P")));
        Assert.Throws<ArgumentNullException>(() => new DiscoveredPrinter(id, new NetworkPrinterEndpoint("host"), null));
    }

    [Fact]
    public void NetworkDiscoveryOptions_DefaultToRawPortScope()
    {
        NetworkPrinterDiscoveryOptions options = new();

        Assert.Empty(options.Hosts);
        Assert.Equal(9100, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ConnectTimeout);
        Assert.Equal(32, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void PrintOptions_DefaultsToSendingUnsupportedOptions()
    {
        PrintOptions options = new();

        Assert.Equal(UnsupportedOptionBehavior.Send, options.OnUnsupported);
    }

    [Fact]
    public void PrintJobInfo_HasNoProgressAndNoDroppedOptionsByDefault()
    {
        PrintJobInfo job = new("42", PrinterId.FromNetwork("printer.local"), PrintJobState.Queued);

        Assert.Null(job.ImpressionsCompleted);
        Assert.Null(job.TotalImpressions);
        Assert.Null(job.Detail);
        Assert.Empty(job.DroppedOptions);
    }
}
