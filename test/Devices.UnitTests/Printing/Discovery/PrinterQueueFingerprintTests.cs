using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class PrinterQueueFingerprintTests
{
    private static PrinterQueueFingerprint Job(int id, string name, int? uptime = 120, string user = "alice") =>
        new(id, name, uptime, null, user);

    [Fact]
    public void IsDiscriminating_RejectsAJobWithNoName() =>
        Assert.False(Job(1, null).IsDiscriminating);

    [Fact]
    public void IsDiscriminating_RejectsAJobWithABlankName() =>
        Assert.False(Job(1, "   ").IsDiscriminating);

    [Theory]
    [InlineData("Document")]
    [InlineData("untitled")]
    [InlineData("Test Page")]
    [InlineData("(stdin)")]
    public void IsDiscriminating_RejectsANamePrintersGiveEveryJob(string name) =>
        Assert.False(Job(1, name).IsDiscriminating);

    [Fact]
    public void IsDiscriminating_RejectsAJobWithNoCreationTime() =>
        Assert.False(Job(1, "invoice-4471.pdf", null).IsDiscriminating);

    [Fact]
    public void IsDiscriminating_AcceptsANamedJobWithAnUptime() =>
        Assert.True(Job(1, "invoice-4471.pdf").IsDiscriminating);

    [Fact]
    public void IsDiscriminating_AcceptsANamedJobWithAWallClockCreationTime() =>
        Assert.True(new PrinterQueueFingerprint(1, "invoice-4471.pdf", null, DateTimeOffset.UnixEpoch, "alice").IsDiscriminating);

    [Fact]
    public void Summarize_ReportsNoAnswerForAnEmptyQueue() =>
        Assert.Null(PrinterQueueFingerprint.Summarize([]));

    [Fact]
    public void Summarize_ReportsNoAnswerForAQueueOfNothingButPlaceholders() =>
        Assert.Null(PrinterQueueFingerprint.Summarize([Job(1, "Document"), Job(2, null)]));

    [Fact]
    public void Summarize_IsTheSameWhateverOrderThePrinterListedTheJobsIn()
    {
        var first = PrinterQueueFingerprint.Summarize([Job(1, "invoice-4471.pdf"), Job(2, "packing-slip.pdf")]);
        var second = PrinterQueueFingerprint.Summarize([Job(2, "packing-slip.pdf"), Job(1, "invoice-4471.pdf")]);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Summarize_TellsTwoJobsApartByTheirCreationUptime() =>
        Assert.NotEqual(
            PrinterQueueFingerprint.Summarize([Job(1, "invoice-4471.pdf", 120)]),
            PrinterQueueFingerprint.Summarize([Job(1, "invoice-4471.pdf", 121)]));

    [Fact]
    public void Summarize_DropsThePlaceholdersAndKeepsTheRest() =>
        Assert.Equal(
            PrinterQueueFingerprint.Summarize([Job(1, "invoice-4471.pdf")]),
            PrinterQueueFingerprint.Summarize([Job(1, "invoice-4471.pdf"), Job(9, "Document")]));
}
