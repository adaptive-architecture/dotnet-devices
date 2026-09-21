#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// An Interleaved 2 of 5 symbol, drawn as PDF rectangles.
/// </summary>
/// <remarks>
/// A barcode is the one mark on a label that says whether the printing is any good, because
/// it fails in a way a person can see and a scanner can measure: a bar narrower than a device
/// pixel, an edge softened into grey, a symbol a millimetre off its stock. The fixture draws
/// one so that "the bars are sharp" and "the label landed where it should" are assertions
/// rather than opinions about a photograph.
/// <para>
/// Interleaved 2 of 5 and not Code 128: its whole encoding is ten five-element patterns, so
/// the symbol below is correct by inspection rather than by trusting a copied table, and it
/// still scans. Every element is the narrow width or twice it, which is what makes the
/// geometry of a rendered page arithmetic a test can do.
/// </para>
/// </remarks>
public static class Barcode
{
    /// <summary>
    /// The five elements of each digit. A set bit is a wide element, and every digit has
    /// exactly two of them, which is the "2 of 5" the symbology is named after.
    /// </summary>
    private static readonly IReadOnlyList<string> Patterns =
    [
        "00110", "10001", "01001", "11000", "00101",
        "10100", "01100", "00011", "10010", "01010",
    ];

    /// <summary>How much wider a wide element is than a narrow one.</summary>
    public const double WideRatio = 2.0;

    /// <summary>The narrow element width the fixture draws with, in points.</summary>
    /// <remarks>
    /// A point is 1/72 inch, so this is about 3 pixels at 150 dots an inch and 6 at 300. It
    /// is deliberately several pixels wide: a bar thinner than one device pixel cannot
    /// survive any resampling, and a test that did not say so would read like an engine
    /// defect.
    /// </remarks>
    public const double NarrowPoints = 1.5;

    /// <summary>The height of the symbol the fixture draws, in points.</summary>
    public const double HeightPoints = 70;

    /// <summary>
    /// The content stream operators that draw the symbol, in black.
    /// </summary>
    /// <param name="digits">An even number of digits.</param>
    /// <param name="x">The left edge, in points from the left of the page.</param>
    /// <param name="y">The bottom edge, in points from the bottom of the page.</param>
    /// <returns>PDF operators that draw one rectangle for each bar.</returns>
    public static string Draw(string digits, double x, double y)
    {
        StringBuilder operators = new();
        _ = operators.Append("0 0 0 rg\n");

        var left = x;
        var isBar = true;
        foreach (var element in Elements(digits))
        {
            var width = element ? NarrowPoints * WideRatio : NarrowPoints;
            if (isBar)
            {
                _ = operators.Append(String.Create(
                    CultureInfo.InvariantCulture,
                    $"{left:0.###} {y:0.###} {width:0.###} {HeightPoints:0.###} re f\n"));
            }

            left += width;
            isBar = !isBar;
        }

        return operators.ToString();
    }

    /// <summary>
    /// How wide the whole symbol is, in points.
    /// </summary>
    /// <param name="digits">An even number of digits.</param>
    /// <returns>The width from the first bar to the last.</returns>
    public static double WidthPoints(string digits)
    {
        var width = 0.0;
        foreach (var element in Elements(digits))
        {
            width += element ? NarrowPoints * WideRatio : NarrowPoints;
        }

        return width;
    }

    /// <summary>
    /// How many bars the symbol has, which is how many dark runs a scan line crosses.
    /// </summary>
    /// <param name="digits">An even number of digits.</param>
    /// <returns>The number of bars.</returns>
    public static int BarCount(string digits)
    {
        var bars = 0;
        var isBar = true;
        foreach (var unused in Elements(digits))
        {
            if (isBar)
            {
                bars++;
            }

            isBar = !isBar;
        }

        return bars;
    }

    // Every element of the symbol in order, starting with a bar and alternating: the start
    // pattern, the digits two at a time with the first drawn as bars and the second as the
    // spaces between them, and the stop pattern.
    // The check is out here rather than in the iterator below, because an iterator does not
    // run until it is enumerated, and a caller that passed an odd number of digits would be
    // told so wherever the sequence happened to be read rather than where it was asked for.
    private static IEnumerable<bool> Elements(string digits)
    {
        if (String.IsNullOrEmpty(digits) || digits.Length % 2 != 0)
        {
            throw new ArgumentException("Interleaved 2 of 5 encodes an even number of digits.", nameof(digits));
        }

        return Encoded(digits);
    }

    private static IEnumerable<bool> Encoded(string digits)
    {
        // Start: narrow bar, narrow space, narrow bar, narrow space.
        yield return false;
        yield return false;
        yield return false;
        yield return false;

        for (var pair = 0; pair < digits.Length; pair += 2)
        {
            var bars = PatternOf(digits[pair]);
            var spaces = PatternOf(digits[pair + 1]);
            for (var element = 0; element < 5; element++)
            {
                yield return bars[element] == '1';
                yield return spaces[element] == '1';
            }
        }

        // Stop: wide bar, narrow space, narrow bar.
        yield return true;
        yield return false;
        yield return false;
    }

    private static string PatternOf(char digit)
    {
        if (digit is < '0' or > '9')
        {
            throw new ArgumentException($"'{digit}' is not a digit.", nameof(digit));
        }

        return Patterns[digit - '0'];
    }
}
