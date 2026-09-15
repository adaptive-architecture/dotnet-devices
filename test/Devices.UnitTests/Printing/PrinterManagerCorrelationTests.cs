using AdaptArch.Devices.Printing;
using AdaptArch.Devices.UnitTests.Printing.Discovery;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterManagerCorrelationTests
{
    private static readonly DiscoveredPrinter First = FakePrinters.Ipp("192.168.1.50", DiscoverySource.NetworkProbe);
    private static readonly DiscoveredPrinter Second = FakePrinters.Ipp("printer.local", DiscoverySource.Mdns);

    private static PrinterManager Manager(FakePrinterFactory factory, PrinterManagerOptions options, params DiscoveredPrinter[] found) =>
        new(new FakeMdnsDiscovery(found), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, new FakePrintJobMonitor([]), options);

    // Every channel this factory opens reports the same queue, so the correlation proves
    // they are one.
    private static FakePrinterFactory SharedQueueFactory()
    {
        Dictionary<PrinterId, FakeQueueEvidenceChannel> made = [];
        List<FakeQueueEvidenceChannel> all = [];
        return new FakePrinterFactory
        {
            EvidenceOf = channel =>
            {
                if (made.TryGetValue(channel.Id, out var existing))
                {
                    return existing;
                }

                FakeQueueEvidenceChannel evidence = new(channel);
                evidence.Queue.Add(new PrinterQueueFingerprint(7, "invoice-4471.pdf", 120, null, "alice"));
                foreach (var peer in all)
                {
                    peer.SharesQueueWith.Add(evidence);
                    evidence.SharesQueueWith.Add(peer);
                }

                all.Add(evidence);
                made[channel.Id] = evidence;
                return evidence;
            },
        };
    }

    [Fact]
    public async Task DiscoverAsync_CorrelatesNoQueueByDefault()
    {
        var factory = SharedQueueFactory();
        var manager = Manager(factory, new PrinterManagerOptions(), First, Second);

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(2, devices.Count);
        Assert.Empty(factory.Evidence);
    }

    [Fact]
    public async Task DiscoverAsync_QueueCorrelationMergesTwoChannelsIntoOneDevice()
    {
        var factory = SharedQueueFactory();
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions() },
            First,
            Second);

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices);
        Assert.Equal(2, device.Channels.Count);
    }

    [Fact]
    public async Task DiscoverAsync_QueueCorrelationKeepsTheOldAddressResolvable()
    {
        var factory = SharedQueueFactory();
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions() },
            First,
            Second);

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices);
        Assert.Contains(First.Id, device.Channels.Select(static channel => channel.Id));
        Assert.Contains(Second.Id, device.Channels.Select(static channel => channel.Id));
    }

    [Fact]
    public async Task DiscoverAsync_ReadsTheCorrelationPolicyFromTheManagerAndNotFromTheArgument()
    {
        var factory = SharedQueueFactory();
        var manager = Manager(factory, new PrinterManagerOptions(), First, Second);

        // The argument scopes one discovery. Consent to open sessions, and to write, is not
        // a scope, so the manager's own policy is the only one that counts.
        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions() },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, devices.Count);
        Assert.Empty(factory.Evidence);
    }

    [Fact]
    public async Task DiscoverAsync_AQueueCorrelationFailureDoesNotFailTheDiscovery()
    {
        FakePrinterFactory factory = new()
        {
            EvidenceOf = channel => new FakeQueueEvidenceChannel(channel) { ReadFailure = new HttpRequestException("no route to host") },
        };
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions { AllowTracerJob = true } },
            First,
            Second);

        Assert.Equal(2, (await manager.DiscoverAsync(null, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task DiscoverAsync_DisposesEveryChannelTheCorrelationOpened()
    {
        var factory = SharedQueueFactory();
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions() },
            First,
            Second);

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.All(factory.Evidence, evidence => Assert.True(evidence.Disposed));
    }

    [Fact]
    public async Task DiscoverAsync_AChannelThatCannotBeOpenedCostsOnlyItsOwnEvidence()
    {
        var shared = SharedQueueFactory();
        FakePrinterFactory factory = new()
        {
            EvidenceOf = channel => channel.Id == First.Id
                ? throw new InvalidOperationException("No printer answers there.")
                : shared.EvidenceOf(channel),
        };
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions() },
            First,
            Second);

        Assert.Equal(2, (await manager.DiscoverAsync(null, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task PrintAsync_CreatesNoTracerWhenItRefreshesToResolveAnIdentifier()
    {
        // Two idle printers. Only a tracer could correlate them, and printing one label must
        // never leave a job on every printer on the network.
        FakePrinterFactory factory = new()
        {
            EvidenceOf = channel => new FakeQueueEvidenceChannel(channel),
        };
        var manager = Manager(
            factory,
            new PrinterManagerOptions { QueueCorrelation = new QueueCorrelationOptions { AllowTracerJob = true } },
            First,
            Second);

        _ = await Assert.ThrowsAnyAsync<Exception>(() => manager.PrintAsync(
            PrinterId.ForRaw("nowhere.local"),
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));

        Assert.All(factory.Evidence, evidence => Assert.Equal(0, evidence.Creates));
    }
}
