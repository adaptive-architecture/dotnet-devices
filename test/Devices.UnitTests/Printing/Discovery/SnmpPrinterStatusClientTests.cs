using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using DotNetSnmp.Asn1.SyntaxObjects;
using Lextm.SharpSnmpLib;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class SnmpPrinterStatusClientTests
{
    private const string Host = "192.168.1.50";

    [Fact]
    public async Task GetDetailsAsync_ReadsNameLocationSerialAndPageCount()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Text(PrinterMibOids.PrinterName, "Lobby LaserJet"),
                SnmpResponses.Text(PrinterMibOids.SystemLocation, "Reception"),
                SnmpResponses.Text(PrinterMibOids.SerialNumber, "CN12345"),
                SnmpResponses.Counter(PrinterMibOids.MarkerLifeCount, 48210),
                SnmpResponses.Integer(PrinterMibOids.PrinterStatus, 3))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Lobby LaserJet", details.Info.Name);
        Assert.Equal("Reception", details.Info.Location);
        Assert.Equal("CN12345", details.Status.SerialNumber);
        Assert.Equal(48210, details.Status.LifetimePageCount);
        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.True(details.Status.IsAcceptingJobs);
        Assert.Equal(PrinterIdKind.Network, details.Info.Id.Kind);
        Assert.Equal(Host, details.Info.Id.Value);
    }

    [Fact]
    public async Task GetDetailsAsync_SendsGetRequestForEveryScalar()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [EmptyWalk()]);

        _ = await NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        var first = factory.Requests[0];
        Assert.Equal(SnmpType.GetRequestPdu, SnmpRequests.ReadType(first));
        Assert.Equal("public", SnmpRequests.ReadCommunity(first));
        Assert.Equal(PrinterMibOids.Scalars, SnmpRequests.ReadOids(first));
    }

    [Fact]
    public async Task GetDetailsAsync_WalksSupplyTableWithGetBulk()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [EmptyWalk()]);

        _ = await NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        var walk = factory.Requests[1];
        Assert.Equal(SnmpType.GetBulkRequestPdu, SnmpRequests.ReadType(walk));
        Assert.Equal(0, SnmpRequests.ReadNonRepeaters(walk));
        Assert.Equal(20, SnmpRequests.ReadMaxRepetitions(walk));
        Assert.Equal(PrinterMibOids.SupplyColumns, SnmpRequests.ReadOids(walk));
    }

    [Fact]
    public async Task GetDetailsAsync_PrinterNameMissing_FallsBackToSystemName()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.NoSuchInstance(PrinterMibOids.PrinterName),
                SnmpResponses.Text(PrinterMibOids.SystemName, "printer-07"))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("printer-07", details.Info.Name);
    }

    [Fact]
    public async Task GetDetailsAsync_NoNameAtAll_FallsBackToHost()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(Host, details.Info.Name);
    }

    // A printer that supplies only a part of the Printer MIB must not fail the query.
    [Fact]
    public async Task GetDetailsAsync_SerialNumberAbsent_ReportsNullWithoutFailing()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Text(PrinterMibOids.PrinterName, "Basic"),
                SnmpResponses.NoSuchObject(PrinterMibOids.SerialNumber))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Null(details.Status.SerialNumber);
        Assert.Null(details.Status.LifetimePageCount);
        Assert.Equal("Basic", details.Info.Name);
    }

    [Theory]
    [InlineData(1, PrinterStatusState.Unknown)]
    [InlineData(2, PrinterStatusState.Unknown)]
    [InlineData(3, PrinterStatusState.Idle)]
    [InlineData(4, PrinterStatusState.Processing)]
    [InlineData(5, PrinterStatusState.Processing)]
    public async Task GetDetailsAsync_MapsHrPrinterStatus(int reported, PrinterStatusState expected)
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars(SnmpResponses.Integer(PrinterMibOids.PrinterStatus, reported))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(expected, details.Status.State);
    }

    [Fact]
    public async Task GetDetailsAsync_JammedBit_ReportsErrorAndDetail()
    {
        // Bit 5 is jammed, counting from the highest bit of the first byte.
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Integer(PrinterMibOids.PrinterStatus, 3),
                SnmpResponses.Bytes(PrinterMibOids.DetectedErrorState, [0x04, 0x00]))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Error, details.Status.State);
        Assert.Equal("jammed", details.Status.Detail);
        Assert.False(details.Status.IsAcceptingJobs);
    }

    [Fact]
    public async Task GetDetailsAsync_OfflineBit_ReportsOffline()
    {
        // Bit 6 is offline.
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Integer(PrinterMibOids.PrinterStatus, 3),
                SnmpResponses.Bytes(PrinterMibOids.DetectedErrorState, [0x02, 0x00]))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Offline, details.Status.State);
        Assert.False(details.Status.IsAcceptingJobs);
    }

    // A printer that is low on toner still prints, so a warning must not change the state.
    [Fact]
    public async Task GetDetailsAsync_LowTonerBit_KeepsStateAndStillReportsDetail()
    {
        // Bit 2 is lowToner.
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Integer(PrinterMibOids.PrinterStatus, 3),
                SnmpResponses.Bytes(PrinterMibOids.DetectedErrorState, [0x20, 0x00]))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal("lowToner", details.Status.Detail);
        Assert.True(details.Status.IsAcceptingJobs);
    }

    [Fact]
    public async Task GetDetailsAsync_SeveralErrorBits_JoinsEveryNameIntoDetail()
    {
        // Bit 1 noPaper, bit 4 doorOpen, and bit 13 inputTrayEmpty.
        FakeSnmpChannelFactory factory = new(
            [Scalars(SnmpResponses.Bytes(PrinterMibOids.DetectedErrorState, [0x48, 0x04]))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("noPaper; doorOpen; inputTrayEmpty", details.Status.Detail);
        Assert.Equal(PrinterStatusState.Error, details.Status.State);
    }

    [Fact]
    public async Task GetDetailsAsync_NoErrorBits_ReportsNoDetail()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars(
                SnmpResponses.Integer(PrinterMibOids.PrinterStatus, 3),
                SnmpResponses.Bytes(PrinterMibOids.DetectedErrorState, [0x00, 0x00]))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Null(details.Status.Detail);
        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
    }

    [Fact]
    public async Task GetDetailsAsync_ReadsMarkersWithLevelAndColor()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Black Toner"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", 1000),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", 250),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesColorantIndex}.1.1", 1),
                SnmpResponses.Text($"{PrinterMibOids.ColorantValue}.1.1", "black"))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        var marker = Assert.Single(details.Status.Markers);
        Assert.Equal("Black Toner", marker.Name);
        Assert.Equal(25, marker.LevelPercent);
        Assert.Equal(250, marker.LevelRaw);
        Assert.Equal(1000, marker.MaxCapacity);
        Assert.Equal("black", marker.Color);
    }

    // -2 means "some amount remains, unknown". LevelPercent cannot express that, but
    // LevelRaw still carries the reported value, so a caller can tell it apart from a
    // marker that reported nothing at all.
    [Fact]
    public async Task GetDetailsAsync_UnknownAmountRemainingLevel_ReportsRawValueWithNullPercent()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Black Ink Bottle"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", 500),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", -2))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        var marker = Assert.Single(details.Status.Markers);
        Assert.Null(marker.LevelPercent);
        Assert.Equal(-2, marker.LevelRaw);
        Assert.Equal(500, marker.MaxCapacity);
    }

    // The agent returns the rows interleaved, one repetition at a time, as RFC 3416 says.
    [Fact]
    public async Task GetDetailsAsync_ReadsSeveralMarkersInTableOrder()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Agent(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Cyan"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", 200),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", 100),
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.2", "Magenta"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.2", 200),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.2", 20))]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(["Cyan", "Magenta"], details.Status.Markers.Select(marker => marker.Name));
        Assert.Equal([50, 10], details.Status.Markers.Select(marker => marker.LevelPercent));
    }

    // The Printer MIB uses -1, -2 and -3 to say that a level is not a quantity.
    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public async Task GetDetailsAsync_NegativeSupplyLevel_ReportsUnknownLevel(int level)
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Toner"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", 1000),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", level))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(details.Status.Markers).LevelPercent);
    }

    [Fact]
    public async Task GetDetailsAsync_NonPositiveMaxCapacity_ReportsUnknownLevel()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Toner"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", -2),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", 500))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(details.Status.Markers).LevelPercent);
    }

    [Fact]
    public async Task GetDetailsAsync_LevelAboveCapacity_ClampsToOneHundred()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Toner"),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.1", 100),
                SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.1", 250))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(100, Assert.Single(details.Status.Markers).LevelPercent);
    }

    [Fact]
    public async Task GetDetailsAsync_EndOfMibView_StopsTheWalk()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(
                SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.1", "Toner"),
                SnmpResponses.EndOfMibView($"{PrinterMibOids.SuppliesLevel}.1.1"))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Toner", Assert.Single(details.Status.Markers).Name);
    }

    // A row that leaves the requested columns means the table is finished.
    [Fact]
    public async Task GetDetailsAsync_RowOutsideColumns_StopsTheWalk()
    {
        FakeSnmpChannelFactory factory = new(
            [Scalars()],
            [Walk(SnmpResponses.Text("1.3.6.1.2.1.99.1.1.1", "unrelated"))]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Empty(details.Status.Markers);
        Assert.Equal(2, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_FirstAttemptSilent_RetriesAndSucceeds()
    {
        FakeSnmpChannelFactory factory = new(
            [],
            [Scalars(SnmpResponses.Text(PrinterMibOids.PrinterName, "Patient"))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Patient", details.Info.Name);
        Assert.Equal(3, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_NoAnswer_ThrowsInvalidOperationAfterEveryAttempt()
    {
        FakeSnmpChannelFactory factory = new();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));

        Assert.Contains(Host, exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_AnswerWithWrongRequestId_IsDiscarded()
    {
        // The first datagram carries an identifier the client never sent, so the client
        // must keep waiting and accept only the second one.
        FakeSnmpChannelFactory factory = new(
            [
                Reply(_ => SnmpResponses.Response(1, SnmpResponses.Text(PrinterMibOids.PrinterName, "Stale"))),
                Scalars(SnmpResponses.Text(PrinterMibOids.PrinterName, "Fresh")),
            ],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Fresh", details.Info.Name);
    }

    [Fact]
    public async Task GetDetailsAsync_AnswerFromAnotherAddress_IsDiscarded()
    {
        FakeSnmpChannelFactory factory = new(
            [
                Reply(
                    requestId => SnmpResponses.Response(requestId, SnmpResponses.Text(PrinterMibOids.PrinterName, "Stray")),
                    IPAddress.Parse("192.168.1.99")),
                Scalars(SnmpResponses.Text(PrinterMibOids.PrinterName, "Real")),
            ],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Real", details.Info.Name);
    }

    // A socket with two address families reports an IPv4 sender as an IPv4-mapped IPv6
    // address. That is still the printer.
    [Fact]
    public async Task GetDetailsAsync_AnswerFromMappedAddress_IsAccepted()
    {
        FakeSnmpChannelFactory factory = new(
            [Reply(
                requestId => SnmpResponses.Response(requestId, SnmpResponses.Text(PrinterMibOids.PrinterName, "Mapped")),
                IPAddress.Parse($"::ffff:{Host}"))],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Mapped", details.Info.Name);
    }

    [Fact]
    public async Task GetDetailsAsync_MalformedAnswerThenGoodAnswer_UsesTheGoodAnswer()
    {
        FakeSnmpChannelFactory factory = new(
            [
                new FakeSnmpAnswer(_ => [0x30, 0x05, 0x02]),
                Scalars(SnmpResponses.Text(PrinterMibOids.PrinterName, "Good")),
            ],
            [EmptyWalk()]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal("Good", details.Info.Name);
        Assert.Equal(2, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_AgentReportsErrorStatus_ThrowsInvalidOperation()
    {
        FakeSnmpChannelFactory factory = new(
            [Reply(requestId => SnmpResponses.Response(requestId, 5, 1, SnmpResponses.Null(PrinterMibOids.SystemName)))]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));
    }

    // A malformed datagram is not an answer, so an agent that sends only malformed
    // datagrams is an agent that never answers.
    [Fact]
    public async Task GetDetailsAsync_OnlyMalformedAnswers_ThrowsInvalidOperationAfterEveryAttempt()
    {
        FakeSnmpChannelFactory factory = new(
            [new FakeSnmpAnswer(_ => [0x30, 0x05, 0x02])],
            [new FakeSnmpAnswer(_ => [0x30, 0x05, 0x02])],
            [new FakeSnmpAnswer(_ => [0x30, 0x05, 0x02])]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));
        Assert.Equal(3, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_SendFails_ReportsTheSocketErrorAsCause()
    {
        SocketException failure = new((int)SocketError.NetworkUnreachable);
        FakeSnmpChannelFactory factory = new() { SendFailure = failure };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));

        Assert.Same(failure, exception.InnerException);
        Assert.Equal(3, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_IPv4Host_OpensAnIPv4Channel()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [EmptyWalk()]);

        _ = await NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.All(factory.AddressFamilies, family => Assert.Equal(AddressFamily.InterNetwork, family));
    }

    [Fact]
    public async Task GetDetailsAsync_IPv6Host_OpensAnIPv6Channel()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [EmptyWalk()]);

        _ = await NewClient(factory).GetDetailsAsync("::1", TestContext.Current.CancellationToken);

        Assert.Equal(2, factory.AttemptCount);
        Assert.All(factory.AddressFamilies, family => Assert.Equal(AddressFamily.InterNetworkV6, family));
    }

    // A real agent answers the five columns interleaved, one row of each per repetition,
    // and a column that runs out spills over into the next one. The client must read all
    // four supplies from that one answer and must not ask again.
    [Fact]
    public async Task GetDetailsAsync_FourSupplies_FinishTheWalkInOneRoundTrip()
    {
        string[] names = ["Black", "Cyan", "Magenta", "Yellow"];
        List<Variable> mib = [];
        for (var row = 1; row <= names.Length; row++)
        {
            mib.Add(SnmpResponses.Text($"{PrinterMibOids.SuppliesDescription}.1.{row}", names[row - 1]));
            mib.Add(SnmpResponses.Integer($"{PrinterMibOids.SuppliesMaxCapacity}.1.{row}", 100));
            mib.Add(SnmpResponses.Integer($"{PrinterMibOids.SuppliesLevel}.1.{row}", 10 * row));
            mib.Add(SnmpResponses.Integer($"{PrinterMibOids.SuppliesColorantIndex}.1.{row}", row));
            mib.Add(SnmpResponses.Text($"{PrinterMibOids.ColorantValue}.1.{row}", names[row - 1].ToLowerInvariant()));
        }

        mib.Add(SnmpResponses.Text("1.3.6.1.2.1.43.13.4.1.1.1.1", "next table"));
        FakeSnmpChannelFactory factory = new([Scalars()], [Agent([.. mib])]);

        var details = await NewClient(factory)
            .GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(2, factory.Requests.Count);
        Assert.Equal(names, details.Status.Markers.Select(marker => marker.Name));
        Assert.Equal([10, 20, 30, 40], details.Status.Markers.Select(marker => marker.LevelPercent));
        Assert.Equal(["black", "cyan", "magenta", "yellow"], details.Status.Markers.Select(marker => marker.Color));
    }

    [Fact]
    public async Task GetDetailsAsync_WalkTooBig_AsksAgainForHalfTheRows()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [TooBig()], [EmptyWalk()]);

        _ = await NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(3, factory.Requests.Count);
        Assert.Equal(20, SnmpRequests.ReadMaxRepetitions(factory.Requests[1]));
        Assert.Equal(10, SnmpRequests.ReadMaxRepetitions(factory.Requests[2]));
        Assert.Equal(PrinterMibOids.SupplyColumns, SnmpRequests.ReadOids(factory.Requests[2]));
    }

    [Fact]
    public async Task GetDetailsAsync_WalkTooBigTwice_ThrowsInvalidOperation()
    {
        FakeSnmpChannelFactory factory = new([Scalars()], [TooBig()], [TooBig()]);

        _ = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));
        Assert.Equal(3, factory.AttemptCount);
    }

    // Only the table walk can ask for less. A scalar read that is too big is an error.
    [Fact]
    public async Task GetDetailsAsync_ScalarsTooBig_ThrowsInvalidOperation()
    {
        FakeSnmpChannelFactory factory = new([TooBig()]);

        _ = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken));
        Assert.Equal(1, factory.AttemptCount);
    }

    [Fact]
    public async Task GetDetailsAsync_CallerCancellation_Propagates()
    {
        FakeSnmpChannelFactory factory = new();
        SnmpPrinterStatusClient client = new(
            new SnmpPrinterStatusOptions { RequestTimeout = TimeSpan.FromMinutes(1) },
            factory.Create);
        using CancellationTokenSource callerSource = new();
        await callerSource.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetDetailsAsync(Host, callerSource.Token));
    }

    [Fact]
    public async Task GetDetailsAsync_BlankHost_Throws()
    {
        FakeSnmpChannelFactory factory = new();

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => NewClient(factory).GetDetailsAsync("  ", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task GetDetailsAsync_PortOutOfRange_Throws(int port)
    {
        FakeSnmpChannelFactory factory = new();

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => NewClient(factory).GetDetailsAsync(Host, TestContext.Current.CancellationToken, port));
    }

    [Fact]
    public void Constructor_NullOptions_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new SnmpPrinterStatusClient(null));

    [Fact]
    public void Constructor_EmptyCommunity_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new SnmpPrinterStatusClient(new SnmpPrinterStatusOptions { Community = String.Empty }));

    [Fact]
    public void Constructor_NegativeRetries_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SnmpPrinterStatusClient(new SnmpPrinterStatusOptions { Retries = -1 }));

    [Fact]
    public void Constructor_NonPositiveTimeout_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SnmpPrinterStatusClient(new SnmpPrinterStatusOptions { RequestTimeout = TimeSpan.Zero }));

    [Fact]
    public void DefaultPort_IsTheIanaSnmpPort() => Assert.Equal(161, SnmpPrinterStatusClient.DefaultPort);

    private static SnmpPrinterStatusClient NewClient(FakeSnmpChannelFactory factory) =>
        new(new SnmpPrinterStatusOptions { RequestTimeout = TimeSpan.FromMilliseconds(150) }, factory.Create);

    // An answer that echoes the request identifier, the way a real agent does.
    private static FakeSnmpAnswer Reply(Func<int, byte[]> response, IPAddress sender = null) =>
        new(request => response(SnmpRequests.ReadRequestId(request)), sender);

    private static FakeSnmpAnswer Scalars(params Variable[] variables) =>
        Reply(requestId => SnmpResponses.Response(requestId, variables));

    private static FakeSnmpAnswer Walk(params Variable[] variables) =>
        Reply(requestId => SnmpResponses.Response(requestId, variables));

    private static FakeSnmpAnswer EmptyWalk() =>
        Reply(requestId => SnmpResponses.Response(
            requestId, SnmpResponses.EndOfMibView("1.3.6.1.2.1.43.99")));

    // Error status 1 is tooBig.
    private static FakeSnmpAnswer TooBig() =>
        Reply(requestId => SnmpResponses.Response(requestId, 1, 0));

    // An agent that answers every GetBulkRequest from the given rows.
    private static FakeSnmpAnswer Agent(params Variable[] mib) =>
        new(request => SnmpResponses.BulkResponse(request, mib));
}
