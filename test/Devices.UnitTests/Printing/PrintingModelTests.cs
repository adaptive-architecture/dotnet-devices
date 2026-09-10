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
        var id = PrinterId.ForRaw("host");
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
        var id = PrinterId.ForSpooler("Q");
        PrinterConfiguration configuration = new(id);

        Assert.Equal(id, configuration.PrinterId);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Empty(configuration.MediaSizes);
        // Not reported is not the same as not supported.
        Assert.Null(configuration.SupportsDuplex);
        Assert.Null(configuration.SupportsColor);
        Assert.Null(configuration.DefaultMediaSize);
    }

    [Fact]
    public void PrintJobInfo_RecordsCreationTime()
    {
        var id = PrinterId.ForSpooler("dev");
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
        var id = PrinterId.ForRaw("host");
        Assert.Throws<ArgumentNullException>(() => new DiscoveredPrinter(id, null, new PrinterInfo(id, "P")));
        Assert.Throws<ArgumentNullException>(() => new DiscoveredPrinter(id, NetworkPrinterEndpoint.Raw("host"), null));
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
    public void PrintOptions_LeavesEveryNewerOptionUnset()
    {
        PrintOptions options = new();

        Assert.Null(options.MediaType);
        Assert.Null(options.OutputBin);
        Assert.Null(options.Quality);
        Assert.Null(options.PageRanges);
        Assert.Null(options.NumberUp);
    }

    [Fact]
    public void PrintOptions_RefusesAPageCountBelowOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintOptions { NumberUp = 0 });

    [Fact]
    public void PrinterConfiguration_ReportsNothingItWasNotGiven()
    {
        PrinterConfiguration configuration = new(PrinterId.ForRaw("printer.local"));

        Assert.Empty(configuration.Media);
        Assert.Empty(configuration.MediaSources);
        Assert.Empty(configuration.MediaTypes);
        Assert.Empty(configuration.OutputBins);
        Assert.Empty(configuration.Qualities);
        Assert.Empty(configuration.NumberUpValues);
        Assert.Null(configuration.SupportsPageRanges);
        Assert.Null(configuration.DefaultMediaSource);
        Assert.Null(configuration.DefaultOrientation);
        Assert.Null(configuration.DefaultResolutionDpi);
    }

    [Fact]
    public void PrinterMedia_KeepsTheNameAndTheWindowsNumber()
    {
        PrinterMedia media = new("A4", 9);

        Assert.Equal("A4", media.Name);
        Assert.Equal(9, media.WindowsPaperNumber);
        Assert.Null(new PrinterMediaSource("tray-1", null).WindowsBinNumber);
    }

    [Fact]
    public void PrinterMedia_RefusesABlankName()
    {
        Assert.Throws<ArgumentException>(() => new PrinterMedia(" ", 9));
        Assert.Throws<ArgumentException>(() => new PrinterMediaSource(" ", 1));
    }

    [Fact]
    public void PageRange_KeepsBothBoundsAndComparesByValue()
    {
        PageRange range = new(2, 5);

        Assert.Equal(2, range.Lower);
        Assert.Equal(5, range.Upper);
        Assert.Equal(new PageRange(2, 5), range);
        Assert.True(range == new PageRange(2, 5));
        Assert.True(range != new PageRange(2, 6));
        Assert.Equal(new PageRange(2, 5).GetHashCode(), range.GetHashCode());
        Assert.Equal("2-5", range.ToString());
    }

    [Fact]
    public void PageRange_RefusesARangeThatIsNotAPageRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRange(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRange(5, 4));
    }

    [Fact]
    public void PrintJobInfo_HasNoProgressAndNoDroppedOptionsByDefault()
    {
        PrintJobInfo job = new("42", PrinterId.ForRaw("printer.local"), PrintJobState.Queued);

        Assert.Null(job.ImpressionsCompleted);
        Assert.Null(job.TotalImpressions);
        Assert.Null(job.Detail);
        Assert.Empty(job.DroppedOptions);
    }

    [Fact]
    public void PrintOptions_DoesNotRequirePassthroughByDefault()
    {
        PrintOptions options = new();

        Assert.False(options.RequirePassthrough);
    }

    [Fact]
    public void PrintOptions_RejectsZeroOrNegativeCopies()
    {
        PrintOptions options = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.Copies = 0);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.Copies = -1);
        options.Copies = 2;
        Assert.Equal(2, options.Copies);
    }
}
