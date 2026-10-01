using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Rasterization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

// Every PrintOptions member is applied or reported as dropped on every channel. A new member
// fails here until EveryOption sets it and the raw channel, which applies nothing, reports it.
public class PrintOptionsCoverageTests
{
    // They steer the library, not the sheet, so no channel reports them.
    private static readonly string[] NotPrinterSettings =
    [
        nameof(PrintOptions.JobName),
        nameof(PrintOptions.RequestingUserName),
        nameof(PrintOptions.DocumentPassword),
        nameof(PrintOptions.OnUnsupported),
    ];

    private static PrintOptions EveryOption() =>
        new()
        {
            Copies = 2,
            Duplex = DuplexMode.LongEdge,
            ColorMode = PrintColorMode.Monochrome,
            Orientation = PrintOrientation.Landscape,
            Scaling = PrintScaling.AutoFit,
            FitArea = PrintFitArea.Physical,
            MediaSource = "tray-1",
            MediaSize = "iso_a4_210x297mm",
            MediaDimensions = new(PrintLength.FromMillimeters(100), PrintLength.FromMillimeters(150)),
            MediaSizeSource = MediaSizeSource.Document,
            Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft },
            Smoothing = false,
            Rendering = PrintRendering.Raster,
            DocumentPassword = "secret",
            MediaType = "labels",
            OutputBin = "top",
            ResolutionDpi = 300,
            Quality = PrintQuality.High,
            PageRanges = [new PageRange(1, 2)],
            ConverterName = "Any",
            NumberUp = 2,
            TextFonts = [PrintFont.FromBytes(RasterDocuments.FontProgramme())],
            JobName = "job",
            RequestingUserName = "me",
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

    private static PropertyInfo[] Settable() =>
        Array.FindAll(typeof(PrintOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance), property => property.CanWrite);

    private static string[] PrinterSettings() =>
        [.. Settable().Select(property => property.Name).Except(NotPrinterSettings).Order(StringComparer.Ordinal)];

    [Fact]
    public void EveryOption_SetsEveryMemberAwayFromItsDefault()
    {
        var every = EveryOption();
        PrintOptions defaults = new();

        Assert.All(Settable(), property => Assert.NotEqual(property.GetValue(defaults), property.GetValue(every)));
    }

    [Fact]
    public void SetOptions_NamesEveryPrinterSetting() =>
        Assert.Equal(PrinterSettings(), PrintOptionValidator.SetOptions(EveryOption()).Order(StringComparer.Ordinal));

    [Fact]
    public async Task RawPrinter_ReportsAndLogsEveryPrinterSetting()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
        var acceptTask = listener.AcceptTcpClientAsync(timeoutSource.Token);
        FakeLoggerFactory log = new();
        RawPrinter printer = new(NetworkPrinterEndpoint.Raw("127.0.0.1", port)) { LoggerFactory = log };

        // An image, because a named converter on anything the library could render fails
        // the job instead of being reported.
        var job = await printer.PrintAsync(
            PrinterPayload.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, PrinterContentTypes.Png),
            EveryOption(),
            timeoutSource.Token);
        using var accepted = await acceptTask;

        Assert.Equal(PrinterSettings(), job.DroppedOptions.Order(StringComparer.Ordinal));
        Assert.All(job.DroppedOptionDetails, dropped =>
        {
            Assert.Equal(PrintOptionStage.Channel, dropped.Stage);
            Assert.Equal("a raw channel sends the bytes with no job template", dropped.Reason);
        });
        Assert.Equal(PrinterSettings().Length, log.WithId(2041).Count);
        Assert.All(log.WithId(2041), entry => Assert.Equal(LogLevel.Warning, entry.Level));
        Assert.Equal(PrinterContentTypes.Png, job.SubmittedContentType);
    }

    [Fact]
    public async Task RawPrinter_ANamedConverter_FailsBeforeConnecting()
    {
        RawPrinter printer = new(NetworkPrinterEndpoint.Raw("127.0.0.1", 9));

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { ConverterName = "PDFium", OnUnsupported = UnsupportedOptionBehavior.Drop },
            TestContext.Current.CancellationToken));

        Assert.Contains("a raw channel sends the bytes as they are", error.Message, StringComparison.Ordinal);
    }
}
