using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class SpoolerPrinterTests
{
    private static readonly SpoolerPrinterEndpoint Endpoint = new("lobby");

    private static PrinterConfiguration Simplex() =>
        new(PrinterId.ForSpooler("lobby")) { SupportsDuplex = false, MediaSizes = ["iso_a4_210x297mm"] };

    [Fact]
    public async Task PrintAsync_PassesTheQueueNameAndThePayloadToTheDriver()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);
        var payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        var job = await printer.PrintAsync(payload, null, TestContext.Current.CancellationToken);

        Assert.Equal("lobby", Assert.Single(driver.SubmittedQueues));
        Assert.Same(payload, Assert.Single(driver.SubmittedPayloads));
        Assert.Equal("11", job.JobId);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsOneTimeAndKeepsTheAnswer()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);

        var first = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);
        var second = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, driver.ConfigurationReads);
    }

    [Fact]
    public async Task PrintAsync_DropNamesTheRemovedOptionOnTheReturnedJob()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Drop },
            TestContext.Current.CancellationToken);

        Assert.Equal([nameof(PrintOptions.Duplex)], job.DroppedOptions);
        Assert.Equal("lobby", Assert.Single(driver.SubmittedQueues));
    }

    [Fact]
    public async Task PrintAsync_ReportsTheOptionsTheDriverDidNotApply()
    {
        // The printer removes Duplex and the driver reports Copies: both names reach the caller.
        FakeSpoolerDriver driver = new(Simplex()) { DroppedOptions = [new(nameof(PrintOptions.Copies), PrintOptionStage.DeviceMode, "no field")] };
        SpoolerPrinter printer = new(Endpoint, driver);

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Copies = 2, Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Drop },
            TestContext.Current.CancellationToken);

        Assert.Equal([nameof(PrintOptions.Duplex), nameof(PrintOptions.Copies)], job.DroppedOptions);
    }

    [Fact]
    public async Task PrintAsync_LogsEachDroppedOptionWithItsStageAndReason()
    {
        FakeLoggerFactory log = new();
        FakeSpoolerDriver driver = new(Simplex()) { DroppedOptions = [new(nameof(PrintOptions.Copies), PrintOptionStage.DeviceMode, "no field")] };
        SpoolerPrinter printer = new(Endpoint, driver) { LoggerFactory = log };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Copies = 2 },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(log.WithId(2041));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Copies", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DeviceMode", entry.Message, StringComparison.Ordinal);
        Assert.Contains("no field", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CupsPrinter_PrintAsync_LogsEachDroppedOption()
    {
        FakeLoggerFactory log = new();
        FakeSpoolerDriver driver = new(Simplex()) { DroppedOptions = [new(nameof(PrintOptions.Smoothing), PrintOptionStage.Channel, "no attribute")] };
        CupsPrinter printer = new(new CupsPrinterEndpoint("cups.local", "lobby"), driver) { LoggerFactory = log };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { Smoothing = false },
            TestContext.Current.CancellationToken);

        Assert.Single(log.WithId(2041));
    }

    [Fact]
    public async Task PrintAsync_ThrowStopsTheJobBeforeItReachesTheDriver()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw },
            TestContext.Current.CancellationToken));

        Assert.Empty(driver.SubmittedQueues);
    }
}
