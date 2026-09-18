#nullable enable
using System.Collections.Generic;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// Stands in for the Windows PDF renderer, which lives in another package and needs the
/// in-box engine. It reports what the spooler asked it for, so the resolution and the page
/// range the driver passes down are visible to a test.
/// </summary>
internal sealed class RecordingPdfConverter : IPrintPayloadConverter
{
    private readonly int _pages;

    public RecordingPdfConverter(int pages) => _pages = pages;

    public string? LastTarget { get; private set; }

    public int LastDpi { get; private set; }

    public IReadOnlyList<PageRange>? LastRanges { get; private set; }

    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        LastTarget = context.TargetContentType;
        LastDpi = context.Dpi;
        LastRanges = context.PageRanges;

        IReadOnlyList<byte[]> pages = [.. Enumerable.Range(0, _pages).Select(page => new byte[] { (byte)page })];
        return Task.FromResult(pages);
    }
}
