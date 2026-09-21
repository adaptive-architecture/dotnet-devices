using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

// A physical length, so the only thing to get wrong is the unit and the rounding.
public class PrintLengthTests
{
    [Fact]
    public void FromMillimeters_CountsHundredths()
    {
        Assert.Equal(150, PrintLength.FromMillimeters(1.5).HundredthsOfMillimeter);
        Assert.Equal(-40, PrintLength.FromMillimeters(-0.4).HundredthsOfMillimeter);
        Assert.Equal(0, PrintLength.Zero.HundredthsOfMillimeter);
    }

    [Fact]
    public void FromInches_CountsTwoThousandFiveHundredAndFortyAnInch()
    {
        Assert.Equal(2540, PrintLength.FromInches(1).HundredthsOfMillimeter);
        Assert.Equal(10160, PrintLength.FromInches(4).HundredthsOfMillimeter);
    }

    [Theory]
    // One inch is the resolution itself, whichever resolution it is.
    [InlineData(203)]
    [InlineData(300)]
    [InlineData(600)]
    public void ToPixels_TurnsAnInchIntoTheResolution(int dpi) =>
        Assert.Equal(dpi, PrintLength.FromInches(1).ToPixels(dpi));

    [Theory]
    // 5 mm at 203 dots an inch is 39.96 pixels, and at 300 it is 59.06.
    [InlineData(5, 203, 40)]
    [InlineData(5, 300, 59)]
    [InlineData(5, 600, 118)]
    [InlineData(-3, 300, -35)]
    public void ToPixels_RoundsToWholePixels(double millimeters, int dpi, int expected) =>
        Assert.Equal(expected, PrintLength.FromMillimeters(millimeters).ToPixels(dpi));

    [Fact]
    public void ToPixels_MovesTheSameDistanceBothWays()
    {
        // Half away from zero: a length that lands on a half must not move further one way
        // than the other, which is what banker's rounding would do.
        var right = PrintLength.FromHundredthsOfMillimeter(127).ToPixels(100);
        var left = PrintLength.FromHundredthsOfMillimeter(-127).ToPixels(100);

        Assert.Equal(5, right);
        Assert.Equal(-5, left);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-300)]
    public void ToPixels_RefusesAResolutionThatIsNotOne(int dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintLength.FromMillimeters(1).ToPixels(dpi));

    [Fact]
    public void Equality_ComparesTheLength()
    {
        Assert.Equal(PrintLength.FromMillimeters(25.4), PrintLength.FromInches(1));
        Assert.NotEqual(PrintLength.FromMillimeters(25.4), PrintLength.FromInches(2));
    }

    [Fact]
    public void ToString_NamesTheUnit() => Assert.Equal("1.5 mm", PrintLength.FromMillimeters(1.5).ToString());
}
