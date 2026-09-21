using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Which part of the sheet an IPP job is fitted into. The margins come from
// "media-col-default"; what happens once they are known is the arithmetic here.
public class IppFitAreaTests
{
    private const int Dpi = 2540; // One hundredth of a millimetre is one pixel.

    private static readonly MediaDimensions Media =
        new(PrintLength.FromHundredthsOfMillimeter(1000), PrintLength.FromHundredthsOfMillimeter(2000));

    private static readonly MediaMargins Margins = new(
        PrintLength.FromHundredthsOfMillimeter(10),
        PrintLength.FromHundredthsOfMillimeter(20),
        PrintLength.FromHundredthsOfMillimeter(30),
        PrintLength.FromHundredthsOfMillimeter(40));

    [Fact]
    public void ResolveFitArea_SubtractsTheMarginsThePrinterCannotMark()
    {
        var area = IppPrinter.ResolveFitArea(new PrintOptions(), Media, Margins, Dpi);

        Assert.NotNull(area);
        Assert.Equal(30, area.Value.X);
        Assert.Equal(10, area.Value.Y);
        Assert.Equal(1000 - 30 - 40, area.Value.Width);
        Assert.Equal(2000 - 10 - 20, area.Value.Height);
    }

    [Fact]
    public void ResolveFitArea_APhysicalFitIsTheWholeSheet() =>
        Assert.Null(IppPrinter.ResolveFitArea(new PrintOptions { FitArea = PrintFitArea.Physical }, Media, Margins, Dpi));

    [Fact]
    public void ResolveFitArea_APrinterThatReportedNoMarginsHasNoneToSubtract()
    {
        Assert.Null(IppPrinter.ResolveFitArea(new PrintOptions(), Media, null, Dpi));
        Assert.Null(IppPrinter.ResolveFitArea(new PrintOptions(), Media, MediaMargins.None, Dpi));
    }

    [Fact]
    public void ResolveFitArea_MarginsThatLeaveNothingToPrintOnAreIgnored()
    {
        MediaMargins swallowing = new(
            PrintLength.FromHundredthsOfMillimeter(2000),
            PrintLength.FromHundredthsOfMillimeter(2000),
            PrintLength.FromHundredthsOfMillimeter(2000),
            PrintLength.FromHundredthsOfMillimeter(2000));

        // A page fitted into nothing prints nothing, so the sheet is the better answer than a
        // job that silently comes out blank.
        Assert.Null(IppPrinter.ResolveFitArea(new PrintOptions(), Media, swallowing, Dpi));
    }

    [Fact]
    public void ResolveFitArea_WithoutAMediaSizeThereIsNothingToInset() =>
        Assert.Null(IppPrinter.ResolveFitArea(new PrintOptions(), null, Margins, Dpi));
}
