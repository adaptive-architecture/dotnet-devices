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
}
