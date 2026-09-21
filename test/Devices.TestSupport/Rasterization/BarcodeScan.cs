#nullable enable
using System.Collections.Generic;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// What a scanner would see along one line of a rendered page.
/// </summary>
/// <param name="DarkRuns">How many separate dark runs the line crosses.</param>
/// <param name="SoftPixels">
/// How many pixels are neither black nor white. This is the anti-alias measurement: smoothing
/// off drives it to zero, and every grey pixel here is one the printer has to halftone.
/// </param>
/// <param name="FirstDarkPixel">Where the first dark run starts, or -1 when the line has none.</param>
/// <param name="LastDarkPixel">Where the last dark run ends, or -1 when the line has none.</param>
public readonly record struct ScanLine(int DarkRuns, int SoftPixels, int FirstDarkPixel, int LastDarkPixel);

public static class BarcodeScan
{
    // Ink and stock. A rendered edge lands between them, and everything between is what the
    // smoothing switch decides.
    private const int Dark = 64;
    private const int Light = 192;

    /// <summary>
    /// Reads one horizontal line of a page.
    /// </summary>
    /// <param name="pixels">The page, packed with no padding between the lines.</param>
    /// <param name="width">The width of the page in pixels.</param>
    /// <param name="bytesPerPixel">One for grayscale, three for red, green, blue.</param>
    /// <param name="row">The line to read, counted from the top.</param>
    /// <returns>What the line holds.</returns>
    public static ScanLine Read(IReadOnlyList<byte> pixels, int width, int bytesPerPixel, int row)
    {
        var runs = 0;
        var soft = 0;
        var first = -1;
        var last = -1;
        var wasDark = false;

        for (var x = 0; x < width; x++)
        {
            var value = Luminance(pixels, ((row * width) + x) * bytesPerPixel, bytesPerPixel);
            var isDark = value <= Dark;
            if (isDark && !wasDark)
            {
                runs++;
                if (first < 0)
                {
                    first = x;
                }
            }

            if (isDark)
            {
                last = x;
            }

            if (value > Dark && value < Light)
            {
                soft++;
            }

            wasDark = isDark;
        }

        return new ScanLine(runs, soft, first, last);
    }

    /// <summary>
    /// How many pixels of a whole page are neither ink nor stock.
    /// </summary>
    /// <param name="pixels">The page, packed with no padding between the lines.</param>
    /// <param name="width">The width of the page in pixels.</param>
    /// <param name="height">The height of the page in pixels.</param>
    /// <param name="bytesPerPixel">One for grayscale, three for red, green, blue.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// One scan line across a barcode says whether its bars are hard. This says how much of
    /// the page as a whole arrived as a half-tone the printer has to resolve, which is where
    /// the smoothing switch shows up on text.
    /// </remarks>
    public static int SoftPixels(IReadOnlyList<byte> pixels, int width, int height, int bytesPerPixel)
    {
        var soft = 0;
        for (var row = 0; row < height; row++)
        {
            soft += Read(pixels, width, bytesPerPixel, row).SoftPixels;
        }

        return soft;
    }

    // The green channel is close enough to luminance for a page that is black on white, and
    // it reads the same whether the pixels are one octet or three.
    private static int Luminance(IReadOnlyList<byte> pixels, int offset, int bytesPerPixel) =>
        bytesPerPixel == 1 ? pixels[offset] : pixels[offset + 1];
}
