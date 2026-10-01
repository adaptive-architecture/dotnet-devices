namespace AdaptArch.Devices.Printing.Synthesis;

// Writes a PNG or a JPEG as a one-page PDF. With a media, the page is that media and the image
// is fitted, anchored and offset on it by ImagePlacement, as the Windows spooler places it;
// without one, or when the job takes its media from the document, the page is the image at
// its own size.
internal static class ImagePdf
{
    // An image that states no density prints as GDI+ reads it, at 96 dpi.
    public const double DefaultImageDpi = 96;

    public static byte[] Write(ReadOnlySpan<byte> image, string contentType, SynthesisLayout layout)
    {
        PdfWriter pdf = new();
        var catalog = pdf.Reserve();
        var pages = pdf.Reserve();
        var page = pdf.Reserve();
        var content = pdf.Reserve();
        var picture = pdf.Reserve();

        var interpolate = layout.Smoothing == false ? String.Empty : "/Interpolate true";
        int width;
        int height;
        double? dpi;
        if (String.Equals(contentType, PrinterContentTypes.Jpeg, StringComparison.OrdinalIgnoreCase))
        {
            var jpeg = JpegInfo.Read(image);
            (width, height, dpi) = (jpeg.Width, jpeg.Height, jpeg.Dpi);
            var space = jpeg.Components == 1 ? "DeviceGray" : jpeg.Components == 3 ? "DeviceRGB" : "DeviceCMYK";
            var decode = jpeg.AdobeInverted ? "/Decode[1 0 1 0 1 0 1 0]" : String.Empty;
            pdf.Stream(picture, $"/Type/XObject/Subtype/Image/Width {width}/Height {height}/ColorSpace/{space}/BitsPerComponent 8{decode}{interpolate}/Filter/DCTDecode", image);
        }
        else
        {
            var png = PngReader.Decode(image);
            (width, height, dpi) = (png.Width, png.Height, png.Dpi);
            var mask = String.Empty;
            if (png.Alpha is not null)
            {
                var alpha = pdf.Reserve();
                pdf.CompressedStream(alpha, $"/Type/XObject/Subtype/Image/Width {width}/Height {height}/ColorSpace/DeviceGray/BitsPerComponent 8{interpolate}", png.Alpha);
                mask = $"/SMask {alpha} 0 R";
            }

            var space = png.Colors == 1 ? "DeviceGray" : "DeviceRGB";
            pdf.CompressedStream(picture, $"/Type/XObject/Subtype/Image/Width {width}/Height {height}/ColorSpace/{space}/BitsPerComponent 8{interpolate}{mask}", png.Color);
        }

        (var pageWidth, var pageHeight, var drawn) = Geometry(width, height, dpi ?? DefaultImageDpi, layout);
        var matrix = $"{PdfWriter.Number(drawn.Width)} 0 0 {PdfWriter.Number(drawn.Height)} {PdfWriter.Number(drawn.X)} {PdfWriter.Number(pageHeight - drawn.Y - drawn.Height)}";
        pdf.Stream(content, String.Empty, System.Text.Encoding.Latin1.GetBytes($"q {matrix} cm /Im0 Do Q\n"));
        pdf.Object(page, $"<</Type/Page/Parent {pages} 0 R/MediaBox[0 0 {PdfWriter.Number(pageWidth)} {PdfWriter.Number(pageHeight)}]/Resources<</XObject<</Im0 {picture} 0 R>>>>/Contents {content} 0 R>>");
        pdf.Object(pages, $"<</Type/Pages/Kids[{page} 0 R]/Count 1>>");
        pdf.Object(catalog, $"<</Type/Catalog/Pages {pages} 0 R>>");
        return pdf.Finish(catalog);
    }

    // The page size and where the image is drawn on it, in points from the top-left corner.
    private static (double Width, double Height, (double X, double Y, double Width, double Height) Drawn) Geometry(
        int width,
        int height,
        double dpi,
        SynthesisLayout layout)
    {
        var naturalWidth = ImagePlacement.NaturalPixels(width, dpi, SynthesisLayout.Dpi);
        var naturalHeight = ImagePlacement.NaturalPixels(height, dpi, SynthesisLayout.Dpi);
        if (layout.Media is not MediaDimensions media || layout.MediaSizeSource == MediaSizeSource.Document)
        {
            var w = Points(naturalWidth);
            var h = Points(naturalHeight);
            return (w, h, (0, 0, w, h));
        }

        var canvasWidth = media.Width.ToPixels(SynthesisLayout.Dpi);
        var canvasHeight = media.Height.ToPixels(SynthesisLayout.Dpi);
        var area = layout.FitArea is { IsEmpty: false } fit ? fit : new ImageRectangle(0, 0, canvasWidth, canvasHeight);
        var placed = ImagePlacement.Compute(
            naturalWidth,
            naturalHeight,
            area.Width,
            area.Height,
            null,
            layout.Scaling,
            new ImagePlacementOptions
            {
                Borderless = area.Width >= canvasWidth && area.Height >= canvasHeight,
                Anchor = layout.Placement?.Anchor ?? PrintAnchor.Center,
                OffsetX = layout.Placement?.OffsetX.ToPixels(SynthesisLayout.Dpi) ?? 0,
                OffsetY = layout.Placement?.OffsetY.ToPixels(SynthesisLayout.Dpi) ?? 0,
            });

        return (
            SynthesisLayout.Points(media.Width),
            SynthesisLayout.Points(media.Height),
            (Points(placed.X + area.X), Points(placed.Y + area.Y), Points(placed.Width), Points(placed.Height)));
    }

    private static double Points(int pixels) => pixels * 72.0 / SynthesisLayout.Dpi;
}
