namespace AdaptArch.Devices.Printing;

/// <summary>
/// One content type the library knows, and what a printer does with it.
/// </summary>
/// <param name="ContentType">The media type, for example <c>image/tiff</c>.</param>
/// <param name="Kind">What a printer does with the bytes.</param>
/// <param name="CommandSet">
/// The IEEE 1284 token a printer reports for this format, for example <c>ZPL</c>. Leave
/// it <c>null</c> when the printer names the format by its media type.
/// </param>
public sealed record PrinterFormat(string ContentType, PrinterFormatKind Kind, string? CommandSet = null);
