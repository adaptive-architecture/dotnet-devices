namespace AdaptArch.Devices.Printing;

/// <summary>
/// A physical length on the media, held in hundredths of a millimetre.
/// </summary>
/// <remarks>
/// The unit is the one IPP measures media in, so nothing is invented on the way to
/// <c>media-col</c>. It is physical and never device pixels: a length in printer units moves
/// on the page when the resolution changes, and a label that moves is a rejected parcel.
/// </remarks>
public readonly record struct PrintLength
{
    /// <summary>How many hundredths of a millimetre make one inch.</summary>
    private const double PerInch = 2540.0;

    private PrintLength(int hundredthsOfMillimeter) => HundredthsOfMillimeter = hundredthsOfMillimeter;

    /// <summary>
    /// Gets the length in hundredths of a millimetre. It is negative for a length measured
    /// leftwards or upwards.
    /// </summary>
    public int HundredthsOfMillimeter { get; }

    /// <summary>Gets the length of zero, which moves nothing.</summary>
    public static PrintLength Zero => default;

    /// <summary>
    /// Creates a length from hundredths of a millimetre.
    /// </summary>
    /// <param name="value">The length, negative for leftwards or upwards.</param>
    /// <returns>The length.</returns>
    public static PrintLength FromHundredthsOfMillimeter(int value) => new(value);

    /// <summary>
    /// Creates a length from millimetres.
    /// </summary>
    /// <param name="value">The length, negative for leftwards or upwards.</param>
    /// <returns>The length, rounded to the nearest hundredth of a millimetre.</returns>
    public static PrintLength FromMillimeters(double value) => new(Round(value * 100.0));

    /// <summary>
    /// Creates a length from inches.
    /// </summary>
    /// <param name="value">The length, negative for leftwards or upwards.</param>
    /// <returns>The length, rounded to the nearest hundredth of a millimetre.</returns>
    public static PrintLength FromInches(double value) => new(Round(value * PerInch));

    /// <summary>
    /// Gets the length in millimetres.
    /// </summary>
    public double Millimeters => HundredthsOfMillimeter / 100.0;

    /// <summary>
    /// Converts the length to device pixels at a resolution.
    /// </summary>
    /// <param name="dpi">The resolution in dots per inch. Must be positive.</param>
    /// <returns>The length in whole pixels, rounded half away from zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resolution is zero or negative.</exception>
    public int ToPixels(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return Round(HundredthsOfMillimeter * dpi / PerInch);
    }

    /// <summary>
    /// Returns the length in millimetres, for a log line or a message.
    /// </summary>
    /// <returns>The length and its unit.</returns>
    public override string ToString() =>
        Millimeters.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm";

    // Half away from zero, so an offset of half a unit moves the same distance in both
    // directions. Banker's rounding would move one of them further than the other.
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
