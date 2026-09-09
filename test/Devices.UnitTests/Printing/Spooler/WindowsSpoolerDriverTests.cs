using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// WindowsSpoolerDriver calls winspool.drv, which does not exist on this platform, so
// every real interop path is untestable here. What IS genuine, executed coverage on
// Linux is the guard at the top of every method: OperatingSystem.IsWindows() is false
// here, so each of the seven ISpoolerDriver members must throw
// PlatformNotSupportedException before it ever reaches native code. That is precisely
// what this file deliberately calls into from an unguarded, cross-platform context, so
// the platform compatibility analyzer's warning here is expected and suppressed.
#pragma warning disable CA1416
public class WindowsSpoolerDriverTests
{
    private readonly WindowsSpoolerDriver _driver = new();

    [Fact]
    public async Task EnumeratePrintersAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.EnumeratePrintersAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task SubmitAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.SubmitAsync("lobby", PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl), null, TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetStatusAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetStatusAsync("lobby", TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetConfigurationAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetConfigurationAsync("lobby", TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetJobsAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetJobsAsync("lobby", TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetJobAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetJobAsync("lobby", "1", TestContext.Current.CancellationToken));

    [Fact]
    public async Task CancelJobAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.CancelJobAsync("lobby", "1", TestContext.Current.CancellationToken));
}
#pragma warning restore CA1416
