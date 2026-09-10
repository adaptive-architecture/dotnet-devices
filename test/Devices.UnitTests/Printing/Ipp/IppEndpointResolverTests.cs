using System.Security.Authentication;
using AdaptArch.Devices.Printing;
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

    [Fact]
    public async Task ResolveAsync_DefaultPolicy_FallsBackToPlainIppAfterATlsFailure()
    {
        // A plain-IPP-only port fails the handshake like a bad certificate does.
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri.Scheme == "https"
                ? throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException())
                : IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null, new IppTransportOptions());

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ipp", uri.Scheme);
    }

    [Fact]
    public async Task ResolveAsync_ValidatingPolicy_ThrowsOnATlsFailureAndSendsNoPlainIpp()
    {
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri.Scheme == "https"
                ? throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException())
                : IppMessages.Ok(IppMessages.Response(0x0000, (0x23, "printer-state", 3))));
        IppTransportOptions options = new() { ServerCertificateValidation = static (_, _, _, _) => false };
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null, options);

        _ = await Assert.ThrowsAsync<AuthenticationException>(() => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        Assert.All(handler.Requests, request => Assert.Equal("https", request.RequestUri!.Scheme));
    }

    [Fact]
    public async Task ResolveAsync_SuppliedClient_ThrowsOnATlsFailure()
    {
        IppMessages.StubHandler handler = new(_ =>
            throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException()));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null, IppTransportOptions.ForSuppliedClient());

        _ = await Assert.ThrowsAsync<AuthenticationException>(() => resolver.ResolveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveAsync_PlainIppNotAllowed_NeverBuildsAPlainIppUri()
    {
        IppMessages.StubHandler handler = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        IppTransportOptions options = new() { AllowPlainIpp = false };
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null, options);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        Assert.All(handler.Requests, request => Assert.Equal("https", request.RequestUri!.Scheme));
        Assert.EndsWith("over IPPS.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_IppNotFoundOnOnePath_TriesTheNextPath()
    {
        // CUPS answers an unknown resource with HTTP 200 and IPP client-error-not-found (0x0406).
        var notFound = IppMessages.Response(0x0406);
        var ok = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri!.AbsolutePath == "/printers/old" ? IppMessages.Ok(notFound) : IppMessages.Ok(ok));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, "printers/old");

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/ipp/print", uri.AbsolutePath);
    }

    [Fact]
    public async Task ResolveAsync_NothingAnswers_KeepsTheLastCauseAsInnerException()
    {
        IppMessages.StubHandler handler = new(_ => throw new HttpRequestException("Connection refused"));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        var inner = Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal("Connection refused", inner.Message);
    }

    [Fact]
    public async Task ResolveAsync_Ipv6Host_BuildsABracketedUri()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "fe80::1", 631, null);

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("[fe80::1]", uri.Host);
        Assert.Equal(631, uri.Port);
    }

    [Fact]
    public async Task RunAsync_TransportFailure_ClearsTheUriSoTheNextCallProbesAgain()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);
        _ = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
        var probes = handler.Requests.Count;

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.RunAsync<int>(
            (_, _) => throw new InvalidOperationException("printer went away"),
            TestContext.Current.CancellationToken));
        _ = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(probes * 2, handler.Requests.Count);
    }
}
