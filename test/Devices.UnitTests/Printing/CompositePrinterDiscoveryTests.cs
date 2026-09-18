using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class CompositePrinterDiscoveryTests
{
    private sealed class StubDiscovery : IPrinterDiscovery
    {
        private readonly IReadOnlyList<DiscoveredPrinter> _printers;
        private readonly Exception _error;

        private StubDiscovery(IReadOnlyList<DiscoveredPrinter> printers, Exception error)
        {
            _printers = printers;
            _error = error;
        }

        public static StubDiscovery Finds(params string[] queues) =>
            new([.. queues.Select(Channel)], null);

        public static StubDiscovery Fails(Exception error) => new([], error);

        public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken) =>
            _error is null ? Task.FromResult(_printers) : Task.FromException<IReadOnlyList<DiscoveredPrinter>>(_error);

        private static DiscoveredPrinter Channel(string queue)
        {
            var id = PrinterId.ForSpooler(queue);
            return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(queue), new PrinterInfo(id, queue));
        }
    }

    [Fact]
    public async Task DiscoverAsync_ReportsWhatEverySourceFound()
    {
        CompositePrinterDiscovery discovery = new(StubDiscovery.Finds("lobby"), StubDiscovery.Finds("desk", "store"));

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["lobby", "desk", "store"], printers.Select(printer => printer.Info.Name));
    }

    // A print server that is down must not hide the queues of the machine itself.
    [Fact]
    public async Task DiscoverAsync_KeepsWhatTheOtherSourcesFoundWhenOneFails()
    {
        CompositePrinterDiscovery discovery = new(
            StubDiscovery.Fails(new HttpRequestException("refused")),
            StubDiscovery.Finds("lobby"));

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal("lobby", Assert.Single(printers).Info.Name);
    }

    [Fact]
    public async Task DiscoverAsync_ThrowsTheFirstFailureWhenEverySourceFailed()
    {
        HttpRequestException first = new("refused");
        CompositePrinterDiscovery discovery = new(StubDiscovery.Fails(first), StubDiscovery.Fails(new TimeoutException()));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            discovery.DiscoverAsync(TestContext.Current.CancellationToken));

        Assert.Same(first, thrown);
    }

    [Fact]
    public void Constructor_RefusesAnEmptySourceList() =>
        Assert.Throws<ArgumentException>(() => new CompositePrinterDiscovery());
}
