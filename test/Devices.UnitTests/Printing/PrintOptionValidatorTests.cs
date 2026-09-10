using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintOptionValidatorTests
{
    private static PrinterConfiguration SimplexA4() =>
        new(PrinterId.FromNetwork("printer.local"))
        {
            SupportsDuplex = false,
            SupportsColor = false,
            MediaSizes = ["iso_a4_210x297mm"],
            SupportedResolutionsDpi = [300],
        };

    [Fact]
    public void Apply_SendPassesEveryOptionThrough()
    {
        var options = new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Send };

        var result = PrintOptionValidator.Apply(options, SimplexA4(), out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_ThrowNamesTheUnsupportedOption()
    {
        var options = new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw };

        var error = Assert.Throws<NotSupportedException>(
            () => PrintOptionValidator.Apply(options, SimplexA4(), out _));

        Assert.Contains("Duplex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DropRemovesTheOptionAndNamesIt()
    {
        var options = new PrintOptions
        {
            Copies = 2,
            Duplex = DuplexMode.LongEdge,
            MediaSize = "na_letter_8.5x11in",
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var result = PrintOptionValidator.Apply(options, SimplexA4(), out var dropped);

        Assert.Null(result.Duplex);
        Assert.Null(result.MediaSize);
        Assert.Equal(2, result.Copies);
        Assert.Equal(["Duplex", "MediaSize"], dropped);

        // The caller's own instance must not be mutated by Drop.
        Assert.Equal(2, options.Copies);
        Assert.Equal(DuplexMode.LongEdge, options.Duplex);
        Assert.Equal("na_letter_8.5x11in", options.MediaSize);
    }

    [Fact]
    public void Apply_AnEmptyConfigurationCannotJudgeAnything()
    {
        var options = new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw };
        var empty = new PrinterConfiguration(PrinterId.FromNetwork("printer.local"));

        var result = PrintOptionValidator.Apply(options, empty, out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_AnEmptyConfigurationCannotJudgeAnythingWithDrop()
    {
        var options = new PrintOptions { ColorMode = PrintColorMode.Color, OnUnsupported = UnsupportedOptionBehavior.Drop };
        var empty = new PrinterConfiguration(PrinterId.FromNetwork("printer.local"));

        var result = PrintOptionValidator.Apply(options, empty, out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_ReturnsNullForNoOptions()
    {
        var result = PrintOptionValidator.Apply(null, SimplexA4(), out var dropped);

        Assert.Null(result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_OptionsWithNoMatchingCapabilityAlwaysPass()
    {
        var options = new PrintOptions
        {
            Copies = 3,
            Orientation = PrintOrientation.Landscape,
            MediaSource = "tray-1",
            JobName = "report",
            OnUnsupported = UnsupportedOptionBehavior.Throw,
        };

        var result = PrintOptionValidator.Apply(options, SimplexA4(), out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_JudgesAPrinterThatReportedOnlyNoDuplexAndNoColor()
    {
        // A monochrome simplex printer with no media list still made a capability statement.
        PrinterConfiguration monochrome = new(PrinterId.FromNetwork("printer.local")) { SupportsDuplex = false, SupportsColor = false };
        var options = new PrintOptions { ColorMode = PrintColorMode.Color, OnUnsupported = UnsupportedOptionBehavior.Drop };

        var result = PrintOptionValidator.Apply(options, monochrome, out var dropped);

        Assert.Equal([nameof(PrintOptions.ColorMode)], dropped);
        Assert.Null(result!.ColorMode);
    }
}
