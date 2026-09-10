using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppPrintJobQueueTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = new("printer.local", 631);
    private static readonly PrinterId Printer = PrinterId.FromNetwork("printer.local");

    [Fact]
    public async Task GetJobsAsync_ReturnsOneEntryForEachJob()
    {
        // 0x21 is integer, 0x23 is enum, 0x42 is nameWithoutLanguage. SharpIppNext groups a
        // GetJobsResponse into one job per attribute-group tag: a repeated "job-id" name
        // inside a single 0x02 group does not start a new job, it is dropped instead
        // (verified empirically against SharpIppNext 4.2.4). A second bare 0x02 tag between
        // the two attribute lists is required to make the printer report two jobs.
        var body = IppMessages.Response(0x0000, 0x02,
            (0x21, "job-id", 1),
            (0x23, "job-state", 5),
            (0x42, "job-name", "first.zpl"),
            (0x02, null, null),
            (0x21, "job-id", 2),
            (0x23, "job-state", 3),
            (0x42, "job-name", "second.zpl"));
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var jobs = await queue.GetJobsAsync(Printer, TestContext.Current.CancellationToken);

        Assert.Equal(2, jobs.Count);
        Assert.Equal("1", jobs[0].JobId);
        Assert.Equal(PrintJobState.Printing, jobs[0].State);
        Assert.Equal("second.zpl", jobs[1].JobName);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenThePrinterDoesNotKnowTheJob()
    {
        // 0x0406 is client-error-not-found.
        var notFound = IppMessages.Response(0x0406, 0x02);
        var ok = IppMessages.Response(0x0000, 0x02);
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            // Request 1 is the resolver probe, which must succeed so resolution finishes.
            // Request 2 is the job lookup, which reports the job as not found.
            requestCount++;
            return requestCount == 1 ? IppMessages.Ok(ok) : IppMessages.Ok(notFound);
        });
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(handler));

        var job = await queue.GetJobAsync(Printer, "9999", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task GetJobAsync_ThrowsForAnIppErrorThatIsNotNotFound()
    {
        // 0x0501 is server-error-operation-not-supported: a real IPP error, but not
        // client-error-not-found. A caller (the polling job monitor) must not read this
        // as "the job left the queue" — it must see the failure.
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, 0x02))
                : IppMessages.Ok(IppMessages.Response(0x0501, 0x02));
        });
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(handler));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.GetJobAsync(Printer, "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenTheIdentifierIsNotANumber()
    {
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok(IppMessages.Response(0x0000, 0x02)))));

        var job = await queue.GetJobAsync(Printer, "not-a-number", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task CancelJobAsync_ReportsTrueOnSuccessAndFalseOnNotFound()
    {
        IppPrintJobQueue ok = new(Endpoint, new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok(IppMessages.Response(0x0000, 0x02)))));

        // 0x0406 is client-error-not-found. Request 1 is the resolver probe, which must
        // succeed so resolution finishes; request 2 is the cancel, which reports not-found.
        var requestCount = 0;
        IppMessages.StubHandler missingHandler = new(_ =>
        {
            requestCount++;
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, 0x02))
                : IppMessages.Ok(IppMessages.Response(0x0406, 0x02));
        });
        IppPrintJobQueue missing = new(Endpoint, new HttpClient(missingHandler));

        Assert.True(await ok.CancelJobAsync(Printer, "1", TestContext.Current.CancellationToken));
        Assert.False(await missing.CancelJobAsync(Printer, "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelJobAsync_ThrowsForAnIppErrorThatIsNotNotFound()
    {
        // 0x0501 is server-error-operation-not-supported: a real IPP error, but not
        // client-error-not-found. See the equivalent GetJobAsync test for why this must
        // throw rather than report false.
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, 0x02))
                : IppMessages.Ok(IppMessages.Response(0x0501, 0x02));
        });
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(handler));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.CancelJobAsync(Printer, "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetJobsAsync_SkipsAJobWithoutAnIdentifier()
    {
        var body = IppMessages.Response(0x0000, 0x02,
            (0x23, "job-state", 5),
            (0x42, "job-name", "orphan.zpl"),
            (0x02, null, null),
            (0x21, "job-id", 2),
            (0x23, "job-state", 3));
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var jobs = await queue.GetJobsAsync(Printer, TestContext.Current.CancellationToken);

        var job = Assert.Single(jobs);
        Assert.Equal("2", job.JobId);
    }
}
