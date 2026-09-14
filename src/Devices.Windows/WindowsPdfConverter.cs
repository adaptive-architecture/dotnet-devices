using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Windows;

// Reads PDF only. The engine behind it is in-box on Windows 10 and later, so the
// converter carries no NuGet dependency of its own.
[SupportedOSPlatform("windows10.0.10240.0")]
internal sealed class WindowsPdfConverter : IPrintPayloadConverter
{
    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return WindowsPdfRenderer.RenderAsync(data, context.Dpi, context.PageRanges, cancellationToken);
    }
}
