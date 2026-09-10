using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// winspool.drv does not exist here, so only the platform guards are testable: every
// member must throw before it reaches native code. The analyzer warning is expected.
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

    [Fact]
    public void UnappliedOptions_NamesEverySetOptionExceptJobName()
    {
        PrintOptions options = new() { Copies = 2, MediaSize = "iso_a4_210x297mm", JobName = "label", OnUnsupported = UnsupportedOptionBehavior.Drop };

        Assert.Equal([nameof(PrintOptions.Copies), nameof(PrintOptions.MediaSize)], WindowsSpoolerDriver.UnappliedOptions(options));
    }

    [Fact]
    public void UnappliedOptions_IsEmptyWithoutOptions() =>
        Assert.Empty(WindowsSpoolerDriver.UnappliedOptions(null));
}
#pragma warning restore CA1416
