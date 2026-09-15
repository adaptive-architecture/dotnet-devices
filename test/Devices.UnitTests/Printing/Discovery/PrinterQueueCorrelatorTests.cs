using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class PrinterQueueCorrelatorTests
{
    private static readonly IReadOnlyList<PrinterScheme> AllTransports = PrinterManagerOptions.DefaultTransports;

    private static DiscoveredPrinter Channel(string host, PrinterScheme scheme = PrinterScheme.Ipp, params PrinterDeviceKey[] aliases)
    {
        var endpoint = scheme == PrinterScheme.Ipps
            ? NetworkPrinterEndpoint.Ipps(host)
            : NetworkPrinterEndpoint.Ipp(host);
        var id = PrinterId.FromEndpoint(endpoint);
        return new DiscoveredPrinter(id, endpoint, new PrinterInfo(id, host)) { Aliases = aliases };
    }

    private static PrinterQueueFingerprint Job(int id, string name, int uptime = 120) =>
        new(id, name, uptime, null, "alice");

    private static (List<PrinterQueueCorrelator.Candidate> Candidates, FakeQueueEvidenceChannel[] Fakes) Build(params DiscoveredPrinter[] channels)
    {
        var fakes = channels.Select(static channel => new FakeQueueEvidenceChannel(channel)).ToArray();
        List<PrinterQueueCorrelator.Candidate> candidates =
            [.. channels.Select((channel, index) => new PrinterQueueCorrelator.Candidate(channel, fakes[index]))];
        return (candidates, fakes);
    }

    private static Task<IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>>> RunAsync(
        List<PrinterQueueCorrelator.Candidate> candidates,
        QueueCorrelationOptions options = null,
        bool allowTracer = true) =>
        PrinterQueueCorrelator.CorrelateAsync(
            candidates,
            options ?? new QueueCorrelationOptions(),
            allowTracer,
            4,
            TestContext.Current.CancellationToken);

    // --- choosing the candidates ------------------------------------------------------

    [Fact]
    public void Choose_SkipsAChannelThatHasNoJobQueue()
    {
        var raw = new DiscoveredPrinter(
            PrinterId.ForRaw("printer.local"),
            NetworkPrinterEndpoint.Raw("printer.local"),
            new PrinterInfo(PrinterId.ForRaw("printer.local"), "printer.local"));

        Assert.DoesNotContain(raw, PrinterQueueCorrelator.Choose([raw, Channel("other.local")], AllTransports));
    }

    [Fact]
    public void Choose_SkipsAChannelOnATransportTheManagerMayNotOpen()
    {
        var chosen = PrinterQueueCorrelator.Choose([Channel("a.local"), Channel("b.local", PrinterScheme.Ipps)], [PrinterScheme.Ipp]);

        Assert.Equal([PrinterScheme.Ipp], chosen.Select(static c => c.Endpoint.Scheme).Distinct());
    }

    [Fact]
    public void Choose_SkipsAChannelAnIdentityAlreadyGrouped()
    {
        var named = Channel("a.local", PrinterScheme.Ipp, PrinterDeviceKey.ForDeviceIdentity("3f2c9a11-0000-4aaa-9bbb-0123456789ab"));

        Assert.DoesNotContain(named, PrinterQueueCorrelator.Choose([named, Channel("b.local")], AllTransports));
    }

    [Fact]
    public void Choose_ReadsOneChannelPerGroupAndNotEveryChannel()
    {
        // Both channels name one host, so they are already one provisional device.
        var chosen = PrinterQueueCorrelator.Choose(
            [Channel("printer.local"), Channel("printer.local", PrinterScheme.Ipps, PrinterDeviceKey.ForHost("printer.local"))],
            AllTransports);

        Assert.Single(chosen);
    }

    // --- stage A ----------------------------------------------------------------------

    [Fact]
    public async Task CorrelateAsync_JoinsTwoChannelsThatReportTheSameQueue()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.Add(Job(7, "invoice-4471.pdf"));
        fakes[1].Queue.Add(Job(7, "invoice-4471.pdf"));

        var proved = await RunAsync(candidates, allowTracer: false);

        Assert.Equal(2, proved.Count);
        Assert.Equal([PrinterDeviceKey.ForHost("b.local")], proved[candidates[0].Channel.Id]);
        Assert.Equal([PrinterDeviceKey.ForHost("a.local")], proved[candidates[1].Channel.Id]);
    }

    [Fact]
    public async Task CorrelateAsync_KeepsTwoChannelsApartWhenTheirQueuesDiffer()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.Add(Job(7, "invoice-4471.pdf"));
        fakes[1].Queue.Add(Job(7, "packing-slip.pdf"));

        Assert.Empty(await RunAsync(candidates, allowTracer: false));
    }

    [Fact]
    public async Task CorrelateAsync_KeepsTwoChannelsApartWhenOnlyOneJobOfManyIsShared()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.AddRange([Job(7, "invoice-4471.pdf"), Job(8, "only-on-a.pdf")]);
        fakes[1].Queue.Add(Job(7, "invoice-4471.pdf"));

        Assert.Empty(await RunAsync(candidates, allowTracer: false));
    }

    [Fact]
    public async Task CorrelateAsync_KeepsTwoIdlePrintersApartWhenBothQueuesAreEmpty() =>
        Assert.Empty(await RunAsync(Build(Channel("a.local"), Channel("b.local")).Candidates, allowTracer: false));

    [Fact]
    public async Task CorrelateAsync_RefusesTheEvidenceOfAJobEveryPrinterCouldHold()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.Add(Job(1, "Document"));
        fakes[1].Queue.Add(Job(1, "Document"));

        Assert.Empty(await RunAsync(candidates, allowTracer: false));
    }

    [Fact]
    public async Task CorrelateAsync_DoesNothingForFewerThanTwoCandidates() =>
        Assert.Empty(await RunAsync(Build(Channel("a.local")).Candidates));

    [Fact]
    public async Task CorrelateAsync_SkipsEverythingWhenMoreChannelsQualifyThanMaxChannels()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.Add(Job(7, "invoice-4471.pdf"));
        fakes[1].Queue.Add(Job(7, "invoice-4471.pdf"));

        Assert.Empty(await RunAsync(candidates, new QueueCorrelationOptions { MaxChannels = 1 }));
        Assert.Equal(0, fakes[0].Reads);
    }

    [Fact]
    public async Task CorrelateAsync_CarriesOneRequestingUserNameThroughout()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true, RequestingUserName = "kiosk-7" });

        Assert.All(fakes[0].UserNames, name => Assert.Equal("kiosk-7", name));
        Assert.All(fakes[1].UserNames, name => Assert.Equal("kiosk-7", name));
    }

    [Fact]
    public async Task CorrelateAsync_LeavesAChannelUntouchedWhenItWillNotAnswer()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].ReadFailure = new HttpRequestException("no route to host");
        fakes[1].Queue.Add(Job(7, "invoice-4471.pdf"));

        Assert.Empty(await RunAsync(candidates, allowTracer: false));
    }

    // --- stage B ----------------------------------------------------------------------

    [Fact]
    public async Task CorrelateAsync_CreatesNoTracerWhenTheTracerIsNotAllowed()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = false });

        Assert.Equal(0, fakes[0].Creates);
        Assert.Equal(0, fakes[1].Creates);
    }

    [Fact]
    public async Task CorrelateAsync_CreatesNoTracerWhenTheCallIsOnlyRefreshingToResolveAnIdentifier()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true }, allowTracer: false);

        Assert.Equal(0, fakes[0].Creates);
    }

    [Fact]
    public async Task CorrelateAsync_CreatesNoTracerOnAChannelWithoutCreateJob()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Support = new QueueTracerSupport(false, true);

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.Equal(0, fakes[0].Creates);
    }

    [Fact]
    public async Task CorrelateAsync_CreatesNoTracerOnAChannelThatCannotHoldAJobIndefinitely()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Support = new QueueTracerSupport(true, false);

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.Equal(0, fakes[0].Creates);
    }

    [Fact]
    public async Task CorrelateAsync_CreatesNoTracerOnAChannelWhoseQueueAlreadyDisprovedTheMatch()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].Queue.Add(Job(7, "only-on-a.pdf"));

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.Equal(0, fakes[0].Creates);
        Assert.Equal(1, fakes[1].Creates);
    }

    [Fact]
    public async Task CorrelateAsync_JoinsTwoChannelsThatBothReportTheTracer()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].SharesQueueWith.Add(fakes[1]);
        fakes[1].SharesQueueWith.Add(fakes[0]);

        var proved = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.Equal(2, proved.Count);
        Assert.Equal([PrinterDeviceKey.ForHost("b.local")], proved[candidates[0].Channel.Id]);
    }

    [Fact]
    public async Task CorrelateAsync_CreatesOneTracerPerChannelAndReadsEachQueueOneMoreTime()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"), Channel("c.local"));

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.All(fakes, fake => Assert.Equal(1, fake.Creates));
        Assert.All(fakes, fake => Assert.Equal(2, fake.Reads));
    }

    [Fact]
    public async Task CorrelateAsync_CancelsEveryTracerWhenTheMatchSucceeds()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].SharesQueueWith.Add(fakes[1]);
        fakes[1].SharesQueueWith.Add(fakes[0]);

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.All(fakes, fake => Assert.Single(fake.Canceled));
    }

    [Fact]
    public async Task CorrelateAsync_CancelsEveryTracerWhenTheSecondReadFails()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].AfterCreate = () =>
        {
            fakes[0].ReadFailure = new HttpRequestException("the printer stopped answering");
            fakes[1].ReadFailure = new HttpRequestException("the printer stopped answering");
            return Task.CompletedTask;
        };

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.All(fakes, fake => Assert.Single(fake.Canceled));
    }

    [Fact]
    public async Task CorrelateAsync_CancelsTheTracerAfterTheCallerCancelled()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        using CancellationTokenSource source = new();
        fakes[0].AfterCreate = () =>
        {
            source.Cancel();
            return Task.CompletedTask;
        };

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PrinterQueueCorrelator.CorrelateAsync(
            candidates,
            new QueueCorrelationOptions { AllowTracerJob = true },
            true,
            1,
            source.Token));

        Assert.Single(fakes[0].Canceled);
    }

    [Fact]
    public async Task CorrelateAsync_CancelsATracerItFoundByNameWhenTheCreateResponseCarriedNoIdentifier()
    {
        (var candidates, var fakes) = Build(Channel("a.local"), Channel("b.local"));
        fakes[0].ReportCreatedJobId = false;

        _ = await RunAsync(candidates, new QueueCorrelationOptions { AllowTracerJob = true });

        Assert.Single(fakes[0].Canceled);
    }
}
