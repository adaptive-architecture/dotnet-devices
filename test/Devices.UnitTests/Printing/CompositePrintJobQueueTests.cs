using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using AdaptArch.Devices.UnitTests.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class CompositePrintJobQueueTests
{
    [Fact]
    public async Task CancelJobAsync_SendsASpoolerIdentifierToTheSpoolerQueue()
    {
        FakeSpoolerDriver driver = new(new PrinterConfiguration(PrinterId.FromSpooler("lobby")));
        CompositePrintJobQueue queue = new(new SpoolerPrintJobQueue(driver), new HttpClient());

        var cancelled = await queue.CancelJobAsync(
            PrinterId.FromSpooler("lobby"), "11", TestContext.Current.CancellationToken);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task GetJobsAsync_RejectsAUsbIdentifier()
    {
        FakeSpoolerDriver driver = new(new PrinterConfiguration(PrinterId.FromSpooler("lobby")));
        CompositePrintJobQueue queue = new(new SpoolerPrintJobQueue(driver), new HttpClient());

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => queue.GetJobsAsync(
            PrinterId.FromUsb("usb-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetJobsAsync_ReusesOneQueueForTheSameNetworkHost()
    {
        // A fresh queue per call would re-run the resolver probe, so the request count
        // tells a reused queue apart from one built per call.
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        FakeSpoolerDriver driver = new(new PrinterConfiguration(PrinterId.FromSpooler("lobby")));
        CompositePrintJobQueue queue = new(new SpoolerPrintJobQueue(driver), new HttpClient(handler));
        var printerId = PrinterId.FromNetwork("printer.local");

        _ = await queue.GetJobsAsync(printerId, TestContext.Current.CancellationToken);
        _ = await queue.GetJobsAsync(printerId, TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);
    }
}
