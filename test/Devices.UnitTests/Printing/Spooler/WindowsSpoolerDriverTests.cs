using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// The public constructor answers the platform question with OperatingSystem.IsWindows(),
// and these check what that answer does. They are therefore the one set in this file that
// only holds off Windows, and they skip themselves on it rather than fail: on Windows the
// same calls reach winspool.drv and succeed.
//
// The guard itself is checked on every platform by WindowsSpoolerDriverSeamTests, which
// passes isWindows explicitly.
public class WindowsSpoolerDriverTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    private readonly WindowsSpoolerDriver _driver = new();

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task EnumeratePrintersAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.EnumeratePrintersAsync(TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task SubmitAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.SubmitAsync("lobby", PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl), null, TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task GetStatusAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetStatusAsync("lobby", TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task GetConfigurationAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetConfigurationAsync("lobby", TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task GetJobsAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetJobsAsync("lobby", TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task GetJobAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.GetJobAsync("lobby", "1", TestContext.Current.CancellationToken));

    [Fact(SkipWhen = nameof(OnWindows), Skip = "The public constructor reads the real platform, and on Windows these calls reach the spooler.")]
    public async Task CancelJobAsync_ThrowsOnANonWindowsPlatform() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => _driver.CancelJobAsync("lobby", "1", TestContext.Current.CancellationToken));
}
