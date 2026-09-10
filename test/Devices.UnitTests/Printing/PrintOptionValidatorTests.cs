using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintOptionValidatorTests
{
    private static PrinterConfiguration SimplexA4() =>
        new(PrinterId.ForRaw("printer.local"))
        {
            SupportsDuplex = false,
            SupportsColor = false,
            MediaSizes = ["iso_a4_210x297mm"],
            SupportedResolutionsDpi = [300],
        };

    // A printer that names its trays, its types, its bins, its qualities and its pages
    // per sheet, and denies a page range.
    private static PrinterConfiguration OneTrayPrinter() =>
        new(PrinterId.ForRaw("printer.local"))
        {
            MediaSources = [new PrinterMediaSource("tray-1", null)],
            MediaTypes = ["stationery"],
            OutputBins = ["face-down"],
            Qualities = [PrintQuality.Normal],
            NumberUpValues = [1, 2],
            SupportsPageRanges = false,
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
        var empty = new PrinterConfiguration(PrinterId.ForRaw("printer.local"));

        var result = PrintOptionValidator.Apply(options, empty, out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_AnEmptyConfigurationCannotJudgeAnythingWithDrop()
    {
        var options = new PrintOptions { ColorMode = PrintColorMode.Color, OnUnsupported = UnsupportedOptionBehavior.Drop };
        var empty = new PrinterConfiguration(PrinterId.ForRaw("printer.local"));

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
        PrinterConfiguration monochrome = new(PrinterId.ForRaw("printer.local")) { SupportsDuplex = false, SupportsColor = false };
        var options = new PrintOptions { ColorMode = PrintColorMode.Color, OnUnsupported = UnsupportedOptionBehavior.Drop };

        var result = PrintOptionValidator.Apply(options, monochrome, out var dropped);

        Assert.Equal([nameof(PrintOptions.ColorMode)], dropped);
        Assert.Null(result!.ColorMode);
    }

    [Theory]
    [InlineData(nameof(PrintOptions.MediaSource))]
    [InlineData(nameof(PrintOptions.MediaType))]
    [InlineData(nameof(PrintOptions.OutputBin))]
    [InlineData(nameof(PrintOptions.Quality))]
    [InlineData(nameof(PrintOptions.NumberUp))]
    [InlineData(nameof(PrintOptions.PageRanges))]
    public void Apply_DropRemovesEveryNewerOptionThePrinterDoesNotSupport(string option)
    {
        var options = Unsupported(option, UnsupportedOptionBehavior.Drop);

        var result = PrintOptionValidator.Apply(options, OneTrayPrinter(), out var dropped);

        Assert.Equal([option], dropped);
        Assert.Null(Value(result, option));
    }

    [Theory]
    [InlineData(nameof(PrintOptions.MediaSource))]
    [InlineData(nameof(PrintOptions.MediaType))]
    [InlineData(nameof(PrintOptions.OutputBin))]
    [InlineData(nameof(PrintOptions.Quality))]
    [InlineData(nameof(PrintOptions.NumberUp))]
    [InlineData(nameof(PrintOptions.PageRanges))]
    public void Apply_ThrowNamesEveryNewerOptionThePrinterDoesNotSupport(string option)
    {
        var options = Unsupported(option, UnsupportedOptionBehavior.Throw);

        var error = Assert.Throws<NotSupportedException>(
            () => PrintOptionValidator.Apply(options, OneTrayPrinter(), out _));

        Assert.Contains(option, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_KeepsEveryNewerOptionThePrinterDoesSupport()
    {
        var options = new PrintOptions
        {
            MediaSource = "tray-1",
            MediaType = "stationery",
            OutputBin = "face-down",
            Quality = PrintQuality.Normal,
            NumberUp = 2,
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var result = PrintOptionValidator.Apply(options, OneTrayPrinter(), out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    // A configuration that reports only a tray list is not an empty one.
    [Fact]
    public void Apply_JudgesAConfigurationThatReportsOnlyTrays()
    {
        var configuration = new PrinterConfiguration(PrinterId.ForRaw("printer.local"))
        {
            MediaSources = [new PrinterMediaSource("tray-1", null)],
        };
        var options = new PrintOptions { MediaSource = "tray-9", OnUnsupported = UnsupportedOptionBehavior.Drop };

        var result = PrintOptionValidator.Apply(options, configuration, out var dropped);

        Assert.Equal([nameof(PrintOptions.MediaSource)], dropped);
        Assert.Null(result.MediaSource);
    }

    private static PrintOptions Unsupported(string option, UnsupportedOptionBehavior behavior)
    {
        var options = new PrintOptions { OnUnsupported = behavior };
        if (option == nameof(PrintOptions.MediaSource))
        {
            options.MediaSource = "tray-9";
        }
        else if (option == nameof(PrintOptions.MediaType))
        {
            options.MediaType = "envelope";
        }
        else if (option == nameof(PrintOptions.OutputBin))
        {
            options.OutputBin = "mailbox-1";
        }
        else if (option == nameof(PrintOptions.Quality))
        {
            options.Quality = PrintQuality.High;
        }
        else if (option == nameof(PrintOptions.NumberUp))
        {
            options.NumberUp = 4;
        }
        else
        {
            options.PageRanges = [new PageRange(1, 2)];
        }

        return options;
    }

    private static object Value(PrintOptions options, string option)
    {
        if (option == nameof(PrintOptions.MediaSource))
        {
            return options.MediaSource;
        }

        if (option == nameof(PrintOptions.MediaType))
        {
            return options.MediaType;
        }

        if (option == nameof(PrintOptions.OutputBin))
        {
            return options.OutputBin;
        }

        if (option == nameof(PrintOptions.Quality))
        {
            return options.Quality;
        }

        if (option == nameof(PrintOptions.NumberUp))
        {
            return options.NumberUp;
        }

        return options.PageRanges;
    }

    [Fact]
    public void Apply_DropsARotationThePrinterDidNotReport()
    {
        PrinterConfiguration configuration = new(PrinterId.ForRaw("printer"))
        {
            SupportedOrientations = [PrintOrientation.Portrait, PrintOrientation.Landscape],
        };
        PrintOptions options = new()
        {
            Orientation = PrintOrientation.ReversePortrait,
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var applied = PrintOptionValidator.Apply(options, configuration, out var dropped);

        Assert.Null(applied.Orientation);
        Assert.Equal([nameof(PrintOptions.Orientation)], dropped);
    }

    [Fact]
    public void Apply_KeepsARotationThePrinterReported()
    {
        PrinterConfiguration configuration = new(PrinterId.ForRaw("printer"))
        {
            SupportedOrientations = [PrintOrientation.Portrait, PrintOrientation.ReversePortrait],
        };
        PrintOptions options = new()
        {
            Orientation = PrintOrientation.ReversePortrait,
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var applied = PrintOptionValidator.Apply(options, configuration, out var dropped);

        Assert.Equal(PrintOrientation.ReversePortrait, applied.Orientation);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_DropsAScalingThePrinterDidNotReport()
    {
        PrinterConfiguration configuration = new(PrinterId.ForRaw("printer"))
        {
            SupportedScalings = [PrintScaling.None, PrintScaling.Fit],
        };
        PrintOptions options = new()
        {
            Scaling = PrintScaling.Fill,
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var applied = PrintOptionValidator.Apply(options, configuration, out var dropped);

        Assert.Null(applied.Scaling);
        Assert.Equal([nameof(PrintOptions.Scaling)], dropped);
    }

    // An empty list is "the printer said nothing", so the option passes unchanged.
    [Fact]
    public void Apply_KeepsScalingWhenThePrinterReportedNoList()
    {
        PrinterConfiguration configuration = new(PrinterId.ForRaw("printer")) { SupportsColor = true };
        PrintOptions options = new()
        {
            Scaling = PrintScaling.Fill,
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var applied = PrintOptionValidator.Apply(options, configuration, out var dropped);

        Assert.Equal(PrintScaling.Fill, applied.Scaling);
        Assert.Empty(dropped);
    }
}
