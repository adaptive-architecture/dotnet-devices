#nullable enable
using System.Linq;
using System.Text;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// The IPP channel against a printer that is not ours. Every other IPP test in this
/// repository answers with bytes we wrote, so it proves the parser agrees with the fixture;
/// these answer with bytes the CUPS project wrote, which is what proves it agrees with IPP.
/// </summary>
[Collection(IppEvePdfFixture.CollectionName)]
public class IppPrintingTests
{
    private readonly IppEvePdfFixture _printer;

    public IppPrintingTests(IppEvePdfFixture printer) => _printer = printer;

    [Fact]
    public async Task GetConfigurationAsync_ReadsWhatThePrinterAdvertises()
    {
        using IppPrinter printer = new(_printer.Endpoint);

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Contains(PrinterContentTypes.Pdf, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(PrinterContentTypes.PwgRaster, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(configuration.MediaSizes);
        Assert.NotEmpty(configuration.SupportedResolutionsDpi);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsAPrinterThatTakesJobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);
        using IppPrinter printer = new(_printer.Endpoint);

        var status = await printer.GetStatusAsync(cancellationToken);

        Assert.Equal(PrinterStatusState.Idle, status.State);
        Assert.True(status.IsAcceptingJobs);
        Assert.Equal(_printer.Id, status.PrinterId);
    }

    [Fact]
    public async Task PrintAsync_APdfThePrinterReads_SendsItUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);
        using IppPrinter printer = new(_printer.Endpoint);
        var pdf = TestDocuments.OnePagePdf();

        var job = await printer.PrintAsync(
            PrinterPayload.FromBytes(pdf, PrinterContentTypes.Pdf),
            new PrintOptions { JobName = "pdf-passthrough" },
            cancellationToken);

        Assert.NotEmpty(job.JobId);

        var documents = await _printer.ReceivedDocumentsAsync(cancellationToken);
        // No converter is registered and the printer reads PDF, so the document must arrive
        // byte for byte: a raster of a PDF the printer could have read is a regression.
        Assert.Contains(documents, document => document.SequenceEqual(pdf));
    }

    [Fact]
    public async Task PrintAsync_APrinterLanguageNoIppPrinterReads_IsRefusedAndNotReTyped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);
        using IppPrinter printer = new(_printer.Endpoint);

        // ZPL is a printer language, so the library never lets the server re-type it: it
        // negotiates, finds nothing, and falls back to application/octet-stream. This
        // printer refuses that, and a refusal is the right answer — a label printed as its
        // own command source is the failure this path exists to avoid.
        var failure = await Assert.ThrowsAsync<PrinterOperationException>(() => printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            null,
            cancellationToken));

        Assert.Equal(_printer.Id, failure.PrinterId);
    }

    [Fact]
    public async Task JobQueue_ListsTheJobItAcceptedAndReadsItBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);
        using IppPrinter printer = new(_printer.Endpoint);
        using HttpClient client = new();
        IppPrintJobQueue queue = new(_printer.Endpoint, client);

        var job = await printer.PrintAsync(
            PrinterPayload.FromBytes(TestDocuments.OnePagePdf(), PrinterContentTypes.Pdf),
            new PrintOptions { JobName = "queue-readback" },
            cancellationToken);

        var read = await queue.GetJobAsync(_printer.Id, job.JobId, cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(job.JobId, read.JobId);
        Assert.Equal(_printer.Id, read.PrinterId);

        // Get-Jobs answers for jobs that are not finished, and this printer finishes at
        // once. A job that is still queued is listed instead, which CupsServerTests checks
        // against a queue that never prints.
        Assert.Equal(PrintJobState.Completed, read.State);
    }

    [Fact]
    public async Task GetIdentityAsync_ReadsWhatThePrinterSaysItIs()
    {
        using IppPrinter printer = new(_printer.Endpoint);

        var identity = await printer.GetIdentityAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(identity);
        // The uuid is how a printer found twice over two channels is recognised as one
        // device, so an identity without it is no identity at all.
        Assert.NotNull(identity.Uuid);
        Assert.NotEmpty(identity.MakeAndModel ?? String.Empty);
    }

    [Fact]
    public async Task GetStatusAsync_WithRawCaptureOn_KeepsTheAttributesTheModelDropped()
    {
        IppTransportOptions options = new() { CaptureRawResponses = true };
        using IppPrinter printer = new(_printer.Endpoint, options);

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(status.RawAttributes);
        Assert.NotEmpty(status.RawAttributes);
    }
}
