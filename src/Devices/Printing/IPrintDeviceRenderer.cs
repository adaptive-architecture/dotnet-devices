namespace AdaptArch.Devices.Printing;

/// <summary>
/// A converter that can also draw a document straight into a printer device context, so the
/// driver receives the drawing rather than one bitmap a page.
/// </summary>
/// <remarks>
/// Implement it on an <see cref="IPrintPayloadConverter"/>. The Windows spooler uses it for a
/// document whose job sets <see cref="PrintOptions.Rendering"/> to
/// <see cref="PrintRendering.Vector"/>, and calls <see cref="IPrintPayloadConverter.ConvertAsync"/>
/// otherwise. No other channel calls it.
/// </remarks>
public interface IPrintDeviceRenderer
{
    /// <summary>
    /// Opens the document and selects its pages, before any print job exists.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="context">The pages, the password and the rest of the conversion request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The open document. The caller disposes it when the job is spooled or abandoned.</returns>
    /// <remarks>
    /// A file that cannot be read, a wrong password or a page selection that selects nothing
    /// fails here, so a job that cannot print never reaches the queue.
    /// </remarks>
    Task<IPrintDeviceDocument> OpenAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A document an <see cref="IPrintDeviceRenderer"/> opened, ready to draw one page at a time.
/// </summary>
/// <remarks>
/// An engine that is not thread-safe may hold itself for as long as the document is open, so
/// dispose it as soon as the job is spooled.
/// </remarks>
public interface IPrintDeviceDocument : IDisposable
{
    /// <summary>Gets the number of selected pages, in the order they print.</summary>
    int PageCount { get; }

    /// <summary>
    /// Gets the size of one selected page as its document declares it.
    /// </summary>
    /// <param name="index">The 0-based index among the selected pages.</param>
    /// <returns>The width and height of the page.</returns>
    MediaDimensions PageSize(int index);

    /// <summary>
    /// Draws one selected page into a device context.
    /// </summary>
    /// <param name="deviceContext">A Windows <c>HDC</c> with the page already started.</param>
    /// <param name="index">The 0-based index among the selected pages.</param>
    /// <param name="target">The rectangle the page covers on the device, in device pixels, after any turn.</param>
    /// <param name="quarterTurns">How many quarter turns clockwise the page is turned: 0 to 3.</param>
    /// <param name="smoothing">Whether the engine smooths text, images and paths.</param>
    void Draw(nint deviceContext, int index, ImageRectangle target, int quarterTurns, bool smoothing);
}
