using AdaptArch.Devices.Printing.Synthesis;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// A TrueType font the library embeds to print characters of plain text, CSV and email that
/// the built-in Courier does not carry.
/// </summary>
/// <remarks>
/// Courier covers the Windows-1252 (Latin-1) characters. A character outside that set is drawn
/// with the first font that has a glyph for it, from <see cref="PrintOptions.TextFonts"/> when
/// the job sets it, and otherwise from <see cref="PrinterManagerOptions.TextFonts"/> and then
/// <see cref="PrintFormatPolicy.AddDefaultTextFont"/>. The whole font is embedded, so a large
/// font makes a large job.
/// </remarks>
public sealed class PrintFont
{
    private PrintFont(TrueTypeFont font) => Font = font;

    /// <summary>
    /// Gets the PostScript name of the font.
    /// </summary>
    public string Name => Font.Name;

    internal TrueTypeFont Font { get; }

    /// <summary>
    /// Reads a font from the bytes of a TrueType (<c>.ttf</c>) file.
    /// </summary>
    /// <param name="data">The font file.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the data is not a TrueType font, is a collection or has CFF outlines, or
    /// when its licence forbids embedding it.
    /// </exception>
    public static PrintFont FromBytes(ReadOnlyMemory<byte> data) => new(TrueTypeFont.Read(data.ToArray()));

    /// <summary>
    /// Reads a font from a TrueType (<c>.ttf</c>) file.
    /// </summary>
    /// <param name="path">The path of the font file.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentException">Thrown as <see cref="FromBytes"/> throws.</exception>
    public static PrintFont FromFile(string path) => new(TrueTypeFont.Read(File.ReadAllBytes(path)));
}
