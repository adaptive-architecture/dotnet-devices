using System.Net;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class IppPrinterStatusClientTests
{
    [Fact]
    public async Task GetDetailsAsync_ParsesStatusInfoAndMarkers()
    {
        var response = IppMessages.Response(0x0000,
            (0x42, "printer-make-and-model", "EPSON L6270 Series"),
            (0x23, "printer-state", 3),
            (0x44, "printer-state-reasons", "none"),
            (0x22, "printer-is-accepting-jobs", (byte)1),
            (0x42, "marker-names", "Black ink"),
            (0x42, null, "Cyan ink"),
            (0x42, null, "Magenta ink"),
            (0x42, null, "Yellow ink"),
            (0x42, "marker-colors", "#000000"),
            (0x42, null, "#00FFFF"),
            (0x42, null, "#FF00FF"),
            (0x42, null, "#FFFF00"),
            (0x21, "marker-levels", 84),
            (0x21, null, 52),
            (0x21, null, 44),
            (0x21, null, -1));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        var details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal("printer.local", details.Info.Id.Value);
        Assert.Equal("EPSON L6270 Series", details.Info.Name);
        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.True(details.Status.IsAcceptingJobs);
        Assert.Null(details.Status.Detail);
        Assert.Equal(4, details.Status.Markers.Count);
        Assert.Equal("Black ink", details.Status.Markers[0].Name);
        Assert.Equal("#000000", details.Status.Markers[0].Color);
        Assert.Equal(84, details.Status.Markers[0].LevelPercent);
        Assert.Equal("Yellow ink", details.Status.Markers[3].Name);
        Assert.Null(details.Status.Markers[3].LevelPercent);
        // The first request is the resolver's probe; the second is the actual attribute read.
        Assert.Equal(2, handler.Requests.Count);
        var request = handler.Requests[1];
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("application/ipp", request.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task GetDetailsAsync_JoinsStateReasonsAndMapsProcessing()
    {
        var response = IppMessages.Response(0x0000,
            (0x23, "printer-state", 4),
            (0x44, "printer-state-reasons", "media-empty"),
            (0x44, null, "toner-low"));
        IppPrinterStatusClient client = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(response))));

        var details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Processing, details.Status.State);
        Assert.Equal("media-empty; toner-low", details.Status.Detail);
        Assert.True(details.Status.IsAcceptingJobs);
        Assert.Equal("printer.local", details.Info.Name);
        Assert.Empty(details.Status.Markers);
    }

    [Fact]
    public async Task GetDetailsAsync_StoppedPrinterIsPausedAndNotAccepting()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 5));
        IppPrinterStatusClient client = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(response))));

        var details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Paused, details.Status.State);
        Assert.False(details.Status.IsAcceptingJobs);
        Assert.Null(details.Status.Detail);
    }

    [Fact]
    public async Task GetDetailsAsync_FallsBackFromHttpsToHttp()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request => request.RequestUri.Scheme == "https"
            ? throw new HttpRequestException("TLS failure")
            : IppMessages.Ok(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        var details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        // The first three requests are the resolver's probe, falling back from https to
        // http; the fourth is the actual attribute read.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("https", handler.Requests[0].RequestUri.Scheme);
        Assert.Equal("https", handler.Requests[1].RequestUri.Scheme);
        Assert.Equal("http", handler.Requests[2].RequestUri.Scheme);
    }

    [Fact]
    public async Task GetDetailsAsync_FallsBackFromMissingResourcePath()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request => request.RequestUri.AbsolutePath == "/ipp/print"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : IppMessages.Ok(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        var details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal("/ipp/port1", handler.Requests[1].RequestUri.AbsolutePath);
    }

    // A DNS-SD browse reports the resource path in the "rp" TXT attribute. Trying it first
    // reaches a printer that serves IPP on neither well-known path.
    [Fact]
    public async Task GetDetailsAsync_ResourcePathFromDiscovery_IsTriedFirst()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request => request.RequestUri.AbsolutePath == "/printers/queue1"
            ? IppMessages.Ok(response)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        var details = await client.GetDetailsAsync(
            "printer.local", CancellationToken.None, IppPrinterStatusClient.DefaultPort, "printers/queue1");

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal("/printers/queue1", handler.Requests[0].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetDetailsAsync_ResourcePathWithLeadingSlash_IsUsedAsGiven()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        _ = await client.GetDetailsAsync(
            "printer.local", CancellationToken.None, IppPrinterStatusClient.DefaultPort, "/ipp/custom");

        Assert.Equal("/ipp/custom", handler.Requests[0].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetDetailsAsync_ResourcePathStillFallsBackToWellKnownPaths()
    {
        var response = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request => request.RequestUri.AbsolutePath == "/ipp/print"
            ? IppMessages.Ok(response)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        var details = await client.GetDetailsAsync(
            "printer.local", CancellationToken.None, IppPrinterStatusClient.DefaultPort, "/wrong");

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal("/wrong", handler.Requests[0].RequestUri.AbsolutePath);
        Assert.Equal("/ipp/print", handler.Requests[1].RequestUri.AbsolutePath);
    }

    // A path that repeats a well-known path must not be requested two times.
    [Fact]
    public async Task GetDetailsAsync_ResourcePathThatRepeatsAWellKnownPath_IsNotDuplicated()
    {
        IppMessages.StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetDetailsAsync(
                "printer.local", CancellationToken.None, IppPrinterStatusClient.DefaultPort, "/ipp/print"));

        // Two schemes multiplied by the two well-known paths, with no repeat.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("/ipp/print", handler.Requests[0].RequestUri.AbsolutePath);
        Assert.Equal("/ipp/port1", handler.Requests[1].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetDetailsAsync_IppErrorStatus_Throws()
    {
        var response = IppMessages.Response(0x0400);
        IppPrinterStatusClient client = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(response))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetDetailsAsync("printer.local", CancellationToken.None));
    }

    [Fact]
    public async Task GetDetailsAsync_UnreachablePrinter_Throws()
    {
        IppMessages.StubHandler handler = new(_ => throw new HttpRequestException("refused"));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetDetailsAsync("printer.local", CancellationToken.None));

        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetDetailsAsync_CanceledToken_ThrowsOperationCanceled()
    {
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(IppMessages.Response(0x0000)));
        IppPrinterStatusClient client = new(new HttpClient(handler));
        using CancellationTokenSource canceledSource = new();
        await canceledSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetDetailsAsync("printer.local", canceledSource.Token));

        Assert.Empty(handler.Requests);
    }

}
