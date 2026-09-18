using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppDocumentFormatTests
{
    [Theory]
    [InlineData(PrinterContentTypes.Zpl)]
    [InlineData(PrinterContentTypes.Epl)]
    [InlineData(PrinterContentTypes.Cpcl)]
    [InlineData(PrinterContentTypes.EscPos)]
    public void IsRawLanguage_IsTrueForEveryPrinterCommandLanguage(string contentType) =>
        Assert.True(IppDocumentFormat.IsRawLanguage(contentType));

    [Theory]
    [InlineData(PrinterContentTypes.Pdf)]
    [InlineData(PrinterContentTypes.Png)]
    [InlineData(PrinterContentTypes.Text)]
    [InlineData(PrinterContentTypes.OctetStream)]
    public void IsRawLanguage_IsFalseForAFormatAnIppServerKnows(string contentType) =>
        Assert.False(IppDocumentFormat.IsRawLanguage(contentType));

    [Fact]
    public void IsRawLanguage_IgnoresTheCaseOfTheContentType() =>
        Assert.True(IppDocumentFormat.IsRawLanguage("APPLICATION/VND.ZEBRA-ZPL"));

    [Fact]
    public void ForCups_ReplacesAPrinterLanguageWithTheRawFormat() =>
        Assert.Equal(IppDocumentFormat.CupsRaw, IppDocumentFormat.ForCups(PrinterContentTypes.Zpl));

    [Fact]
    public void ForCups_LeavesAFormatCupsKnowsUnchanged() =>
        Assert.Equal(PrinterContentTypes.Pdf, IppDocumentFormat.ForCups(PrinterContentTypes.Pdf));

    [Fact]
    public void Negotiate_LeavesAFormatTheServerKnowsUnchanged() =>
        Assert.Equal(
            PrinterContentTypes.Pdf,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Pdf, [IppDocumentFormat.CupsRaw]));

    [Fact]
    public void Negotiate_KeepsTheLanguageWhenThePrinterNamesItItself() =>
        Assert.Equal(
            PrinterContentTypes.Zpl,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Zpl, [PrinterContentTypes.Pdf, PrinterContentTypes.Zpl]));

    [Fact]
    public void Negotiate_PrefersTheLanguageOverTheRawFormat() =>
        Assert.Equal(
            PrinterContentTypes.Zpl,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Zpl, [IppDocumentFormat.CupsRaw, PrinterContentTypes.Zpl]));

    [Fact]
    public void Negotiate_ChoosesTheCupsRawFormatWhenTheServerOffersIt() =>
        Assert.Equal(
            IppDocumentFormat.CupsRaw,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Zpl, [PrinterContentTypes.Pdf, IppDocumentFormat.CupsRaw]));

    [Fact]
    public void Negotiate_FallsBackToOctetStreamForAPrinterThatIsNotCups() =>
        Assert.Equal(
            PrinterContentTypes.OctetStream,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Zpl, [PrinterContentTypes.Pdf, PrinterContentTypes.OctetStream]));

    [Fact]
    public void Negotiate_FallsBackToOctetStreamWhenThePrinterReportedNoFormats() =>
        Assert.Equal(
            PrinterContentTypes.OctetStream,
            IppDocumentFormat.Negotiate(PrinterContentTypes.Zpl, []));

    // A vendor language an application declared is protected the same way ZPL is: CUPS
    // must apply no filter to it, or it prints the command source as text.
    [Fact]
    public void Negotiate_ProtectsALanguageTheApplicationRegistered()
    {
        PrintFormatPolicy policy = new([new PrinterFormat("application/vnd.star-line", PrinterFormatKind.RawLanguage)], null);

        Assert.Equal(
            IppDocumentFormat.CupsRaw,
            IppDocumentFormat.Negotiate("application/vnd.star-line", [IppDocumentFormat.CupsRaw], policy));
        Assert.Equal(
            "application/vnd.star-line",
            IppDocumentFormat.Negotiate("application/vnd.star-line", [IppDocumentFormat.CupsRaw]));
    }

    [Fact]
    public void NegotiateConversionTarget_ChoosesPwgRasterWhenThePrinterReadsIt() =>
        Assert.Equal(
            PrinterContentTypes.PwgRaster,
            IppDocumentFormat.NegotiateConversionTarget(
                [PrinterContentTypes.Jpeg, PrinterContentTypes.PwgRaster],
                new StubConverter(PrinterContentTypes.PwgRaster)));

    [Fact]
    public void NegotiateConversionTarget_IsNullWhenThePrinterReadsNoTargetAtAll() =>
        Assert.Null(IppDocumentFormat.NegotiateConversionTarget(
            [PrinterContentTypes.Jpeg, PrinterContentTypes.Pdf],
            new StubConverter(PrinterContentTypes.PwgRaster)));

    // The printer reads the target, but this converter does not write it.
    [Fact]
    public void NegotiateConversionTarget_IsNullWhenTheConverterWritesOnlyPng() =>
        Assert.Null(IppDocumentFormat.NegotiateConversionTarget(
            [PrinterContentTypes.PwgRaster],
            new StubConverter(PrinterContentTypes.Png)));

    // A printer that reported no list converts nothing, because a raster is a worse job
    // than the document whenever the document would have been read.
    [Fact]
    public void NegotiateConversionTarget_IsNullWhenThePrinterReportedNoFormats() =>
        Assert.Null(IppDocumentFormat.NegotiateConversionTarget(
            [],
            new StubConverter(PrinterContentTypes.PwgRaster)));

    [Fact]
    public void NegotiateConversionTarget_MatchesTheFormatWithoutRegardToCase() =>
        Assert.Equal(
            PrinterContentTypes.PwgRaster,
            IppDocumentFormat.NegotiateConversionTarget(
                ["IMAGE/PWG-RASTER"],
                new StubConverter(PrinterContentTypes.PwgRaster)));

    private sealed class StubConverter : IPrintPayloadConverter
    {
        private readonly string _target;

        public StubConverter(string target)
        {
            _target = target;
        }

        public bool CanConvert(string contentType) =>
            String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

        public bool CanEmit(string targetContentType) =>
            String.Equals(targetContentType, _target, StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The negotiation must not convert.");
    }
}
