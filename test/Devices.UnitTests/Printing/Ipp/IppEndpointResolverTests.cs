using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppEndpointResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsTheFirstUriThatAnswers()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri.Scheme == "https"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ipp", uri.Scheme);
        Assert.Equal("/ipp/print", uri.AbsolutePath);
    }

    [Fact]
    public async Task ResolveAsync_KeepsTheAnswerAndSendsNoSecondRequest()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var first = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
        var second = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResolveAsync_TriesTheCallerPathFirst()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, "printers/lobby");

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/printers/lobby", uri.AbsolutePath);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsWhenNothingAnswers()
    {
        IppMessages.StubHandler handler = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        Assert.Contains("printer.local:631", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_NonNotFoundHttpStatus_AbortsAndReportsTheStatus()
    {
        IppMessages.StubHandler handler = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        Assert.Contains("500", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_CallerCancellation_PropagatesAsOperationCanceled()
    {
        using CancellationTokenSource cts = new();
        IppMessages.StubHandler handler = new(_ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            return IppMessages.Ok(IppMessages.Response(0x0000, (0x23, "printer-state", 3)));
        });
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync(cts.Token));
    }
}
