using System.Diagnostics.CodeAnalysis;
using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppQueueCorrelationTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = NetworkPrinterEndpoint.Ipp("printer.local");
    private static readonly Uri Uri = new("ipp://printer.local:631/ipp/print");

    // IppPrinter implements IQueueEvidenceChannel explicitly, so the interface is the only
    // way to reach these members. This helper exists to make that cast once.
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "IppPrinter implements IQueueEvidenceChannel explicitly, so the concrete type exposes none of the members under test.")]
    private static IQueueEvidenceChannel Evidence(IppPrinter printer) => printer;

    private static string TextOf(byte[] body) => Encoding.UTF8.GetString(body);

    [Fact]
    public async Task ReadQueueAsync_AsksForNotCompletedJobsOfEveryUserAndOnlyTheAttributesTheFingerprintNeeds()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        IppMessages.CapturingHandler handler = new(body);
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await Evidence(printer).ReadQueueAsync("kiosk-7", TestContext.Current.CancellationToken);

        // The resolver probes the endpoint first, so the operation is the last body sent.
        var request = TextOf(handler.RequestBodies[^1]);
        Assert.Contains("which-jobs", request, StringComparison.Ordinal);
        Assert.Contains("not-completed", request, StringComparison.Ordinal);
        Assert.Contains("my-jobs", request, StringComparison.Ordinal);
        Assert.Contains("kiosk-7", request, StringComparison.Ordinal);
        foreach (var attribute in IppQueueFingerprintMapper.RequestedAttributes)
        {
            Assert.Contains(attribute, request, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReadQueueAsync_ReadsBothTheUptimeAndTheWallClockCreationTime()
    {
        // 0x21 integer, 0x42 nameWithoutLanguage, 0x31 dateTime.
        var body = IppMessages.Response(0x0000, 0x02,
            (0x21, "job-id", 7),
            (0x23, "job-state", 3),
            (0x42, "job-name", "invoice-4471.pdf"),
            (0x42, "job-originating-user-name", "alice"),
            (0x21, "time-at-creation", 120));
        using IppPrinter printer = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var queue = await Evidence(printer).ReadQueueAsync("alice", TestContext.Current.CancellationToken);

        var job = Assert.Single(queue);
        Assert.Equal(7, job.JobId);
        Assert.Equal("invoice-4471.pdf", job.JobName);
        Assert.Equal(120, job.UptimeAtCreation);
        Assert.Equal("alice", job.OriginatingUser);
        Assert.True(job.IsDiscriminating);
    }

    [Fact]
    public async Task ReadQueueAsync_SkipsAJobWithoutAnIdentifier()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x42, "job-name", "invoice-4471.pdf"));
        using IppPrinter printer = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        Assert.Empty(await Evidence(printer).ReadQueueAsync("alice", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadTracerSupportAsync_ReportsSupportOnlyWhenBothAnswersAreThere()
    {
        // 0x23 enum: 0x05 is Create-Job. 0x44 keyword.
        var both = IppMessages.Response(0x0000,
            (0x23, "operations-supported", 0x05),
            (0x44, "job-hold-until-supported", "indefinite"));
        var noHold = IppMessages.Response(0x0000,
            (0x23, "operations-supported", 0x05),
            (0x44, "job-hold-until-supported", "no-hold"));
        var noCreate = IppMessages.Response(0x0000,
            (0x23, "operations-supported", 0x02),
            (0x44, "job-hold-until-supported", "indefinite"));

        Assert.True(await SupportAsync(both));
        Assert.False(await SupportAsync(noHold));
        Assert.False(await SupportAsync(noCreate));
    }

    private static async Task<bool> SupportAsync(byte[] body)
    {
        using IppPrinter printer = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));
        var support = await Evidence(printer).ReadTracerSupportAsync(TestContext.Current.CancellationToken);
        return support.IsUsable;
    }

    [Fact]
    public async Task CreateTracerJobAsync_HoldsTheJobIndefinitelyAndSendsNoDocumentAtAll()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 41), (0x23, "job-state", 4));
        IppMessages.CapturingHandler handler = new(body);
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var jobId = await Evidence(printer).CreateTracerJobAsync("adaptarch-devices-correlation-abc", "kiosk-7", TestContext.Current.CancellationToken);

        Assert.Equal("41", jobId);
        var request = TextOf(handler.RequestBodies[^1]);
        Assert.Contains("job-hold-until", request, StringComparison.Ordinal);
        Assert.Contains("indefinite", request, StringComparison.Ordinal);
        Assert.Contains("adaptarch-devices-correlation-abc", request, StringComparison.Ordinal);

        // The whole safety of the correlation: an IPP request that carries no document ends
        // at the end-of-attributes tag, so there is nothing after it for a printer to print.
        Assert.DoesNotContain("document-format", request, StringComparison.Ordinal);
        Assert.Equal(0x03, handler.RequestBodies[^1][^1]);
    }

    [Fact]
    public async Task CancelJobAsync_CarriesTheRequestingUserNameOnlyWhenOneIsGiven()
    {
        var ok = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 41), (0x23, "job-state", 7));
        IppMessages.CapturingHandler handler = new(ok);
        using HttpClient client = new(handler);

        _ = await IppRequests.CancelJobAsync(new IppContext(client), Uri, "41", "kiosk-7", TestContext.Current.CancellationToken);
        _ = await IppRequests.CancelJobAsync(new IppContext(client), Uri, "41", TestContext.Current.CancellationToken);

        Assert.Contains("kiosk-7", TextOf(handler.RequestBodies[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("kiosk-7", TextOf(handler.RequestBodies[1]), StringComparison.Ordinal);
    }
}
