namespace AdaptArch.Devices.Printing.Synthesis;

// What a channel knows of the sheet a synthesized document is laid out on. Lengths that are
// pixels are at Dpi, which is fine enough that rounding never shows on paper.
internal sealed record SynthesisLayout
{
    public const int Dpi = 720;

    // A4, for a channel that knows no media at all.
    public static MediaDimensions DefaultMedia { get; } = new(PrintLength.FromMillimeters(210), PrintLength.FromMillimeters(297));

    public MediaDimensions? Media { get; init; }

    public ImageRectangle? FitArea { get; init; }

    public PrintScaling? Scaling { get; init; }

    public PrintPlacement? Placement { get; init; }

    public bool? Smoothing { get; init; }

    public MediaSizeSource MediaSizeSource { get; init; }

    public IReadOnlyList<PrintFont> Fonts { get; init; } = [];

    public static double Points(PrintLength length) => length.HundredthsOfMillimeter * 72.0 / 2540.0;
}
