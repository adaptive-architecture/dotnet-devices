#nullable enable
using System.Collections.Generic;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// Stands in for the GDI image path so the driver's decision — raw or drawn, and with which
/// device mode — is testable without drawing anything.
/// </summary>
internal sealed class FakeWindowsGdiImagePrinter : IWindowsGdiImagePrinter
{
    public List<WindowsGdiJob> Jobs { get; } = [];

    public List<IReadOnlyList<byte[]>> Pages { get; } = [];

    public int JobId { get; set; } = 42;

    public Exception? Failure { get; set; }

    public int Print(WindowsGdiJob job, byte[] bytes) => PrintPages(job, [bytes]);

    public int PrintPages(WindowsGdiJob job, IReadOnlyList<byte[]> pages)
    {
        Jobs.Add(job);
        Pages.Add(pages);

        return Failure is null ? JobId : throw Failure;
    }
}
