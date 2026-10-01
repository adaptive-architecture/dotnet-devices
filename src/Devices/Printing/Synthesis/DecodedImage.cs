namespace AdaptArch.Devices.Printing.Synthesis;

// Eight-bit samples, grey or RGB, packed with no padding, and the alpha apart when the image has one.
internal sealed class DecodedImage
{
    public DecodedImage(int width, int height, int colors, bool hasAlpha, double? dpi)
    {
        Width = width;
        Height = height;
        Colors = colors;
        Dpi = dpi;
        Color = new byte[width * height * colors];
        Alpha = hasAlpha ? new byte[width * height] : null;
    }

    public int Width { get; }

    public int Height { get; }

    public int Colors { get; }

    public double? Dpi { get; }

    public byte[] Color { get; }

    public byte[]? Alpha { get; }
}
