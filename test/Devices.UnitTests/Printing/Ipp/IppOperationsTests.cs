using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppOperationsTests
{
    private static readonly Uri Printer = new("ipp://printer.local:631/ipp/print");

    [Fact]
    public async Task SendAsync_MapsAnIppErrorToInvalidOperationException()
    {
        // 0x0400 is client-error-bad-request.
        var body = IppMessages.Response(0x0400);
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));

        Assert.Contains("printer.local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MapsAMalformedAnswerToInvalidDataException()
    {
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok([0x09, 0x09, 0x00]))));

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NamesTheNoResponseCaseInsteadOfAnEmptyStatusCode()
    {
        // No HttpStatusCode at all, the way a refused connection reports (no CUPS daemon
        // listening on Linux, for example): the message must not format the missing code
        // as an empty "HTTP ." and must instead say plainly that nothing answered.
        ThrowingHandler handler = new(new HttpRequestException("Connection refused"));
        IppOperations operations = new(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain("HTTP .", error.Message, StringComparison.Ordinal);
        Assert.Contains("no HTTP response", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_KeepsTheRawResponseForMarkerReads()
    {
        var body = IppMessages.Response(0x0000, (0x42, "marker-names", "Black ink"));
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        _ = await operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken);

        Assert.NotNull(operations.LastRawResponse);
    }

    private static GetPrinterAttributesRequest NewRequest() =>
        new() { OperationAttributes = new() { PrinterUri = Printer } };

    // Throws before any HttpResponseMessage exists, the way a refused TCP connection does,
    // so IppOperations sees an HttpRequestException with no HttpStatusCode at all.
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw _exception;
    }

    [Fact]
    public async Task SendAsync_HttpErrorWithAnIppBody_NamesTheHttpStatus()
    {
        // A printer that needs authentication answers 401 with an IPP body. SharpIppNext
        // parses the body and throws with the HttpRequestException as inner exception.
        var body = IppMessages.Response(0x0000);
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized) { Content = new ByteArrayContent(body) })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));

        Assert.Contains("401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ClientTimeout_ThrowsTimeoutException()
    {
        // HttpClient reports its own timeout as a TaskCanceledException while the caller
        // token is not cancelled.
        ThrowingHandler handler = new(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        IppOperations operations = new(new HttpClient(handler));

        _ = await Assert.ThrowsAsync<TimeoutException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));
    }
}
