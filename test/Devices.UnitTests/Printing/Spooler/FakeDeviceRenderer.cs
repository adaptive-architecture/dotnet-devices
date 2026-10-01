#nullable enable
using System.Collections.Generic;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// Stands in for the PDFium converter, which draws a document into a device context as well
/// as rendering it to images. It records which of the two the spooler asked for.
/// </summary>
internal sealed class FakeDeviceRenderer : IPrintPayloadConverter, IPrintDeviceRenderer
{
    private readonly int _pages;

    public FakeDeviceRenderer(int pages) => _pages = pages;

    public string Name => "Drawing";

    public Exception? OpenFailure { get; init; }

    public List<FakeDeviceDocument> Opened { get; } = [];

    public PrintConversionContext? LastOpenContext { get; private set; }

    public int Conversions { get; private set; }

    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        Conversions++;
        IReadOnlyList<byte[]> pages = [.. Enumerable.Range(0, _pages).Select(page => new byte[] { (byte)page })];
        return Task.FromResult(pages);
    }

    public Task<IPrintDeviceDocument> OpenAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        LastOpenContext = context;
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        FakeDeviceDocument document = new(_pages);
        Opened.Add(document);
        return Task.FromResult<IPrintDeviceDocument>(document);
    }
}

/// <summary>
/// A document that records each page it is asked to draw, and whether it was disposed.
/// </summary>
internal sealed class FakeDeviceDocument : IPrintDeviceDocument
{
    public FakeDeviceDocument(int pages) => PageCount = pages;

    public int PageCount { get; }

    // A4 portrait, as a PDF declares it.
    public MediaDimensions Size { get; init; } = new(PrintLength.FromMillimeters(210), PrintLength.FromMillimeters(297));

    public Exception? DrawFailure { get; init; }

    public List<(nint DeviceContext, int Index, ImageRectangle Target, int QuarterTurns, bool Smoothing)> Draws { get; } = [];

    public bool Disposed { get; private set; }

    public MediaDimensions PageSize(int index) => Size;

    public void Draw(nint deviceContext, int index, ImageRectangle target, int quarterTurns, bool smoothing)
    {
        Draws.Add((deviceContext, index, target, quarterTurns, smoothing));
        if (DrawFailure is not null)
        {
            throw DrawFailure;
        }
    }

    public void Dispose() => Disposed = true;
}
