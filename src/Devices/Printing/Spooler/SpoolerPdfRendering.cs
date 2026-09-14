namespace AdaptArch.Devices.Printing.Spooler;

// The PDF renderer lives in AdaptArch.Devices.Windows, because only a -windows
// target can see the in-box Windows.Data.Pdf engine. That package sets the hook
// below, and a null value means PDF is refused with a message that names the
// package. A delegate keeps both sides trim- and AOT-safe, because it needs no
// reflection and no dynamic loading.
internal static class SpoolerPdfRendering
{
    internal static Func<byte[], int, IReadOnlyList<PageRange>?, CancellationToken, Task<IReadOnlyList<byte[]>>>? RenderAsync { get; set; }
}
