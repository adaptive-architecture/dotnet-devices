#nullable enable
using AdaptArch.Devices.Rasterization;
using AdaptArch.Devices.Windows;
using Xunit;

namespace AdaptArch.Devices.Windows.UnitTests;

/// <summary>
/// The same four cases as <c>PdfiumRasterScenarioTests</c>, rendered by the in-box engine.
/// </summary>
/// <remarks>
/// The engine is WinRT and ships with the operating system, so this half runs on Windows
/// only and skips everywhere else rather than failing. The PNGs land under
/// <c>artifacts/rasterization/Windows/</c>, beside the ones PDFium wrote, so a run on Windows
/// leaves both to compare.
/// </remarks>
public class WindowsPdfRasterScenarioTests
{
    [Fact]
    public async Task EveryScenario_RendersWhatThePrinterWouldHaveHadToShow()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            Assert.Skip("The in-box PDF engine ships with Windows 10 and later, and this machine has none.");
        }

        var sheet = await RasterScenarios.RunAsync(
            WindowsPrinting.PdfConverter,
            WindowsPrinting.PdfConverter.Name,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(sheet), $"No contact sheet at {sheet}.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Rendered pages: {sheet}");
    }
}
