namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// The image print path, as the spooler driver sees it.
/// </summary>
/// <remarks>
/// A seam one level above <see cref="IWindowsGdiInterop"/>. The driver decides whether a
/// job goes out raw or through GDI, and that decision, the device mode it builds for each
/// and the job identifier it reports are worth testing without drawing anything.
/// </remarks>
internal interface IWindowsGdiImagePrinter
{
    int Print(WindowsGdiJob job, byte[] bytes);

    int PrintPages(WindowsGdiJob job, IReadOnlyList<byte[]> pages);
}
