using AdaptArch.Devices.Printing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class DiscoveryLogTests
{
    private const string Category = "AdaptArch.Devices.Printing.Discovery";

    [Fact]
    public async Task CorrelateAsync_OverTheChannelLimit_IsAWarning()
    {
        // A network with many channels gets no correlation at all, and nothing else says
        // so: the devices simply stay separate.
        FakeLoggerFactory factory = new();
        List<PrinterQueueCorrelator.Candidate> candidates = [];
        for (var i = 0; i < 5; i++)
        {
            var channel = FakePrinters.Ipp($"printer{i}.local", DiscoverySource.Mdns);
            candidates.Add(new PrinterQueueCorrelator.Candidate(channel, new FakeQueueEvidenceChannel(channel)));
        }

        _ = await PrinterQueueCorrelator.CorrelateAsync(
            candidates,
            new QueueCorrelationOptions { MaxChannels = 2 },
            false,
            4,
            factory.CreateLogger(Category),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(factory.WithId(3030));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("5", entry.Message, StringComparison.Ordinal);
        Assert.Contains("2", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrelateAsync_FewerThanTwoChannels_StaysAtDebug()
    {
        FakeLoggerFactory factory = new();

        _ = await PrinterQueueCorrelator.CorrelateAsync(
            [],
            new QueueCorrelationOptions(),
            false,
            4,
            factory.CreateLogger(Category),
            TestContext.Current.CancellationToken);

        Assert.Equal(LogLevel.Debug, Assert.Single(factory.WithId(3031)).Level);
        Assert.Empty(factory.WithId(3030));
    }

    [Fact]
    public void MalformedPacketReporter_NamesTheFirstFewAndCountsTheRest()
    {
        // One broken device can send thousands of packets in one browse. The address of the
        // sender is what identifies it, so it is kept for the first few.
        MalformedPacketReporter reporter = new();
        System.Net.IPEndPoint sender = new(System.Net.IPAddress.Parse("10.0.0.5"), 5353);

        List<bool> reported = [];
        for (var i = 0; i < 10; i++)
        {
            reported.Add(reporter.ShouldReport(sender));
        }

        Assert.Equal(3, reported.FindAll(static named => named).Count);
        Assert.Equal(10, reporter.Count);
        Assert.Equal(1, reporter.SenderCount);
        Assert.True(reporter.HasMore);
    }

    [Fact]
    public void MalformedPacketReporter_ASmallNumberNeedsNoSummary()
    {
        MalformedPacketReporter reporter = new();

        Assert.True(reporter.ShouldReport(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5353)));

        Assert.False(reporter.HasMore);
    }
}
