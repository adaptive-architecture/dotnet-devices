#nullable enable
using System.Collections.Generic;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// A converter that reads PDF and writes only PWG Raster, as one written for an IPP printer
/// would. It exists to reach the Windows spooler, which chooses a converter by what it
/// reads and draws only PNG.
/// </summary>
internal sealed class RasterOnlyPdfConverter : IPrintPayloadConverter
{
    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    public bool CanEmit(string targetContentType) =>
        String.Equals(targetContentType, PrinterContentTypes.PwgRaster, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The spooler must not ask this converter for a page.");
}
