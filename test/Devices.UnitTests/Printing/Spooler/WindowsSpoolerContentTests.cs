using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// No P/Invoke: the routing only reads strings and numbers, so it runs on any platform.
public class WindowsSpoolerContentTests
{
    [Theory]
    [InlineData("application/pdf", "Document")]
    [InlineData("APPLICATION/PDF", "Document")]
    [InlineData("image/png", "Image")]
    [InlineData("image/jpeg", "Image")]
    [InlineData("application/vnd.zebra-zpl", "Raw")]
    [InlineData("application/octet-stream", "Raw")]
    [InlineData("text/plain", "Raw")]
    public void Classify_RoutesByContentType(string contentType, string expected)
    {
        Assert.Equal(expected, WindowsSpoolerContent.Classify(contentType).ToString());
    }

    [Fact]
    public void Classify_ReadsTheFormatsTheApplicationRegistered()
    {
        PrintFormatPolicy policy = new([new PrinterFormat("image/tiff", PrinterFormatKind.Image)], null);

        Assert.Equal(SpoolerContentKind.Image, WindowsSpoolerContent.Classify("image/tiff", policy));
        Assert.Equal(SpoolerContentKind.Raw, WindowsSpoolerContent.Classify("image/tiff"));
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData(300, 300)]
    [InlineData(72, 150)]
    [InlineData(1200, 600)]
    public void RenderDpi_ClampsToTheRenderableBand(int? resolutionDpi, int expected)
    {
        Assert.Equal(expected, WindowsSpoolerContent.RenderDpi(resolutionDpi));
    }

    [Fact]
    public void SelectPages_NullRangesPrintTheWholeDocument()
    {
        Assert.Equal([0, 1, 2], WindowsSpoolerContent.SelectPages(3, null));
    }

    [Fact]
    public void SelectPages_IntersectsRangesWithTheDocument()
    {
        IReadOnlyList<PageRange> ranges = [new PageRange(2, 4), new PageRange(3, 9)];

        Assert.Equal([1, 2, 3, 4], WindowsSpoolerContent.SelectPages(5, ranges));
    }

    [Fact]
    public void SelectPages_ARangePastTheEndContributesNothing()
    {
        IReadOnlyList<PageRange> ranges = [new PageRange(9, 12)];

        Assert.Empty(WindowsSpoolerContent.SelectPages(5, ranges));
    }

    [Fact]
    public void SelectPages_NoPagesSelectsNothing()
    {
        Assert.Empty(WindowsSpoolerContent.SelectPages(0, null));
    }
}
