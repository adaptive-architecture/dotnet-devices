using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterSchemesTests
{
    [Theory]
    // A raw channel always sends the bytes through unchanged, whatever the port.
    [InlineData(PrinterScheme.Raw, false, true)]
    [InlineData(PrinterScheme.Raw, true, true)]
    // IPP may filter or rasterise the job.
    [InlineData(PrinterScheme.Ipp, false, false)]
    [InlineData(PrinterScheme.Ipp, true, false)]
    [InlineData(PrinterScheme.Ipps, false, false)]
    [InlineData(PrinterScheme.Ipps, true, false)]
    // The Windows spooler sends RAW through. CUPS does not give the promise: submitting a
    // printer language as application/vnd.cups-raw stops the text filters, but a queue
    // with a driver, and a driverless queue, still convert the job for the device.
    [InlineData(PrinterScheme.Spooler, true, true)]
    [InlineData(PrinterScheme.Spooler, false, false)]
    public void GivesPassthrough_ReadsTheSchemeAndThePlatform(PrinterScheme scheme, bool isWindows, bool expected) =>
        Assert.Equal(expected, PrinterSchemes.GivesPassthrough(scheme, isWindows));

    [Theory]
    [InlineData(PrinterScheme.Raw, true, PrintOptionSupport.None)]
    [InlineData(PrinterScheme.Raw, false, PrintOptionSupport.None)]
    [InlineData(PrinterScheme.Ipp, true, PrintOptionSupport.All)]
    [InlineData(PrinterScheme.Ipps, false, PrintOptionSupport.All)]
    // CUPS carries every option, and a Windows device mode carries what has a field.
    [InlineData(PrinterScheme.Spooler, false, PrintOptionSupport.All)]
    [InlineData(
        PrinterScheme.Spooler,
        true,
        PrintOptionSupport.JobName | PrintOptionSupport.Copies | PrintOptionSupport.Duplex
        | PrintOptionSupport.ColorMode | PrintOptionSupport.Orientation | PrintOptionSupport.MediaSource
        | PrintOptionSupport.MediaSize | PrintOptionSupport.ResolutionDpi | PrintOptionSupport.Quality
        | PrintOptionSupport.Scaling)]
    public void SupportedOptions_ReadsTheSchemeAndThePlatform(PrinterScheme scheme, bool isWindows, PrintOptionSupport expected) =>
        Assert.Equal(expected, PrinterSchemes.SupportedOptions(scheme, isWindows));

    [Theory]
    [InlineData(PrinterScheme.Ipp, true)]
    [InlineData(PrinterScheme.Ipps, true)]
    [InlineData(PrinterScheme.Spooler, true)]
    [InlineData(PrinterScheme.Raw, false)]
    public void HasJobQueue_ReadsTheScheme(PrinterScheme scheme, bool expected) =>
        Assert.Equal(expected, PrinterSchemes.HasJobQueue(scheme));

    [Theory]
    [InlineData(PrinterScheme.Raw, 9100)]
    [InlineData(PrinterScheme.Ipp, 631)]
    [InlineData(PrinterScheme.Ipps, 631)]
    [InlineData(PrinterScheme.Spooler, 0)]
    public void DefaultPort_IsThePortOfTheScheme(PrinterScheme scheme, int expected) =>
        Assert.Equal(expected, PrinterSchemes.DefaultPort(scheme));

    [Theory]
    [InlineData(PrinterScheme.Raw, true)]
    [InlineData(PrinterScheme.Ipp, true)]
    [InlineData(PrinterScheme.Ipps, true)]
    [InlineData(PrinterScheme.Spooler, false)]
    public void IsNetwork_SaysWhetherTheSchemeAddressesAHost(PrinterScheme scheme, bool expected) =>
        Assert.Equal(expected, PrinterSchemes.IsNetwork(scheme));

    [Theory]
    [InlineData("raw", PrinterScheme.Raw)]
    [InlineData("RAW", PrinterScheme.Raw)]
    [InlineData("ipp", PrinterScheme.Ipp)]
    [InlineData("ipps", PrinterScheme.Ipps)]
    [InlineData("spooler", PrinterScheme.Spooler)]
    public void TryParse_ReadsASchemeWithoutRegardToCase(string text, PrinterScheme expected)
    {
        Assert.True(PrinterSchemes.TryParse(text, out var scheme));
        Assert.Equal(expected, scheme);
        Assert.Equal(text.ToLowerInvariant(), PrinterSchemes.Format(scheme));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp")]
    [InlineData("lpd")]
    [InlineData("snmp")]
    public void TryParse_RefusesAnythingElse(string text) =>
        Assert.False(PrinterSchemes.TryParse(text, out _));

    [Fact]
    public void PreferenceRank_PutsTheQueueBearingChannelsFirst()
    {
        // A job sent over a channel with a queue can be watched afterwards; one sent over
        // the raw channel cannot, so the raw channel is the last resort.
        Assert.True(PrinterSchemes.PreferenceRank(PrinterScheme.Ipps) < PrinterSchemes.PreferenceRank(PrinterScheme.Ipp));
        Assert.True(PrinterSchemes.PreferenceRank(PrinterScheme.Ipp) < PrinterSchemes.PreferenceRank(PrinterScheme.Spooler));
        Assert.True(PrinterSchemes.PreferenceRank(PrinterScheme.Spooler) < PrinterSchemes.PreferenceRank(PrinterScheme.Raw));
    }
}
