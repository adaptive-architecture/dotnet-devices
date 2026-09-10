using System.Linq;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class WindowsSpoolerCapabilityParserTests
{
    private const int BlockLength = 64;

    private static char[] Block(string name)
    {
        var block = new char[BlockLength];
        Array.Fill(block, '\0');
        name?.CopyTo(block);
        return block;
    }

    // No null terminator: the parser must fall back to the full block length.
    private static char[] FullBlockWithNoTerminator()
    {
        var block = new char[BlockLength];
        Array.Fill(block, 'A');
        return block;
    }

    [Fact]
    public void ParsePaperNames_TrimsAShortNamePaddedWithNulls()
    {
        var buffer = Block("A4");

        var names = WindowsSpoolerCapabilityParser.ParsePaperNames(buffer, 1);

        Assert.Equal(["A4"], names);
    }

    [Fact]
    public void ParsePaperNames_KeepsTheFullNameWhenItFillsTheBlock()
    {
        var buffer = FullBlockWithNoTerminator();

        var names = WindowsSpoolerCapabilityParser.ParsePaperNames(buffer, 1);

        Assert.Equal([new string('A', BlockLength)], names);
    }

    [Fact]
    public void ParsePaperNames_ReturnsAnEmptyStringForAnEmptyBlock()
    {
        var buffer = Block(null);

        var names = WindowsSpoolerCapabilityParser.ParsePaperNames(buffer, 1);

        Assert.Equal([""], names);
    }

    [Fact]
    public void ParsePaperNames_ReadsSeveralBlocksInSequence()
    {
        var buffer = new char[BlockLength * 3];
        Block("A4").CopyTo(buffer, 0);
        FullBlockWithNoTerminator().CopyTo(buffer, BlockLength);
        Block("Letter").CopyTo(buffer, BlockLength * 2);

        var names = WindowsSpoolerCapabilityParser.ParsePaperNames(buffer, 3);

        Assert.Equal(["A4", new string('A', BlockLength), "Letter"], names);
    }

    [Fact]
    public void ParsePaperNames_ReturnsEmptyForZeroCount() =>
        Assert.Empty(WindowsSpoolerCapabilityParser.ParsePaperNames([], 0));

    [Fact]
    public void ParseResolutions_ReadsTheHorizontalValueFromEachPair()
    {
        int[] pairs = [600, 600, 300, 300, 1200, 600];

        var resolutions = WindowsSpoolerCapabilityParser.ParseResolutions(pairs);

        Assert.Equal([600, 300, 1200], resolutions);
    }

    [Fact]
    public void ParseResolutions_ReadsASinglePair()
    {
        int[] pairs = [203, 203];

        var resolutions = WindowsSpoolerCapabilityParser.ParseResolutions(pairs);

        Assert.Equal([203], resolutions);
    }

    [Fact]
    public void ParseResolutions_ReturnsEmptyForAnEmptyBuffer() =>
        Assert.Empty(WindowsSpoolerCapabilityParser.ParseResolutions([]));

    private const int BinBlockLength = 24;

    private static char[] BinBlock(string name)
    {
        var block = new char[BinBlockLength];
        Array.Fill(block, '\0');
        name?.CopyTo(block);
        return block;
    }

    private static IReadOnlyList<PrinterMedia> A4AndLetter() =>
        [new PrinterMedia("A4", 9), new PrinterMedia("Letter", 1)];

    private static IReadOnlyList<PrinterMediaSource> UpperAndManual() =>
        [new PrinterMediaSource("Upper tray", 1), new PrinterMediaSource("Manual feed", 4)];

    [Fact]
    public void ParseNames_ReadsThe24CharacterBinBlocks()
    {
        var buffer = BinBlock("Upper tray").Concat(BinBlock("Manual feed")).ToArray();

        var names = WindowsSpoolerCapabilityParser.ParseNames(buffer, 2, BinBlockLength);

        Assert.Equal(["Upper tray", "Manual feed"], names);
    }

    [Fact]
    public void ParseNames_FallsBackToTheFullBlockWhenABinNameFillsIt()
    {
        var block = new char[BinBlockLength];
        Array.Fill(block, 'B');

        var names = WindowsSpoolerCapabilityParser.ParseNames(block, 1, BinBlockLength);

        Assert.Equal(new string('B', BinBlockLength), Assert.Single(names));
    }

    // DeviceCapabilities answers with WORD values, which Marshal.Copy reads as short.
    [Fact]
    public void ParseWords_UndoesTheSignOfEveryWord()
    {
        var words = new short[] { 1, 9, unchecked((short)0x8001) };

        var values = WindowsSpoolerCapabilityParser.ParseWords(words);

        Assert.Equal([1, 9, 0x8001], values);
    }

    [Fact]
    public void ParseWords_ReturnsAnEmptyListForAnEmptyBuffer() =>
        Assert.Empty(WindowsSpoolerCapabilityParser.ParseWords([]));

    [Fact]
    public void PairMedia_PutsEachNumberBesideItsName()
    {
        var media = WindowsSpoolerCapabilityParser.PairMedia(["A4", "Letter"], [9, 1]);

        Assert.Equal(["A4", "Letter"], media.Select(static entry => entry.Name));
        Assert.Equal([9, 1], media.Select(static entry => entry.WindowsPaperNumber));
    }

    // The two lists come from two separate calls, so they can disagree.
    [Fact]
    public void PairMedia_LeavesANameWithNoNumberUnnumbered()
    {
        var media = WindowsSpoolerCapabilityParser.PairMedia(["A4", "Letter", "A5"], [9]);

        Assert.Equal([9, null, null], media.Select(static entry => entry.WindowsPaperNumber));
    }

    [Fact]
    public void PairMedia_IgnoresANumberWithNoName()
    {
        var media = WindowsSpoolerCapabilityParser.PairMedia(["A4"], [9, 1, 11]);

        Assert.Equal(9, Assert.Single(media).WindowsPaperNumber);
    }

    [Fact]
    public void PairMediaSources_PutsEachBinNumberBesideItsName()
    {
        var sources = WindowsSpoolerCapabilityParser.PairMediaSources(["Upper tray", "Manual feed"], [1]);

        Assert.Equal(["Upper tray", "Manual feed"], sources.Select(static source => source.Name));
        Assert.Equal([1, null], sources.Select(static source => source.WindowsBinNumber));
    }

    [Fact]
    public void ReadDefaults_ReadsEveryFieldThatItsBitAnnounces()
    {
        var fields = WindowsSpoolerCapabilityParser.DmPaperSize
            | WindowsSpoolerCapabilityParser.DmDefaultSource
            | WindowsSpoolerCapabilityParser.DmOrientation
            | WindowsSpoolerCapabilityParser.DmPrintQuality;

        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            fields, 2, 9, 4, 600, 0, A4AndLetter(), UpperAndManual());

        Assert.Equal("A4", defaults.MediaSize);
        Assert.Equal("Manual feed", defaults.MediaSource);
        Assert.Equal(PrintOrientation.Landscape, defaults.Orientation);
        Assert.Equal(600, defaults.ResolutionDpi);
    }

    // A field with no bit holds nothing, whatever value it happens to carry.
    [Fact]
    public void ReadDefaults_IgnoresAFieldWithNoBit()
    {
        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            0, 2, 9, 4, 600, 300, A4AndLetter(), UpperAndManual());

        Assert.Null(defaults.MediaSize);
        Assert.Null(defaults.MediaSource);
        Assert.Null(defaults.Orientation);
        Assert.Null(defaults.ResolutionDpi);
    }

    [Fact]
    public void ReadDefaults_ReportsNoNameForANumberNoListExplains()
    {
        var fields = WindowsSpoolerCapabilityParser.DmPaperSize | WindowsSpoolerCapabilityParser.DmDefaultSource;

        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            fields, 0, 256, 99, 0, 0, A4AndLetter(), UpperAndManual());

        Assert.Null(defaults.MediaSize);
        Assert.Null(defaults.MediaSource);
    }

    // A negative dmPrintQuality is a DMRES_* name, not a number of dots.
    [Fact]
    public void ReadDefaults_FallsBackToTheVerticalResolutionForAQualityName()
    {
        var fields = WindowsSpoolerCapabilityParser.DmPrintQuality | WindowsSpoolerCapabilityParser.DmYResolution;

        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            fields, 0, 0, 0, -1, 300, A4AndLetter(), UpperAndManual());

        Assert.Equal(300, defaults.ResolutionDpi);
    }

    [Fact]
    public void ReadDefaults_ReportsNoResolutionWhenNeitherFieldHoldsOne()
    {
        var fields = WindowsSpoolerCapabilityParser.DmPrintQuality | WindowsSpoolerCapabilityParser.DmYResolution;

        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            fields, 0, 0, 0, -4, 0, A4AndLetter(), UpperAndManual());

        Assert.Null(defaults.ResolutionDpi);
    }

    [Theory]
    [InlineData(1, PrintOrientation.Portrait)]
    [InlineData(2, PrintOrientation.Landscape)]
    public void ReadDefaults_TranslatesEveryOrientation(short value, PrintOrientation expected)
    {
        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            WindowsSpoolerCapabilityParser.DmOrientation, value, 0, 0, 0, 0, [], []);

        Assert.Equal(expected, defaults.Orientation);
    }

    [Fact]
    public void ReadDefaults_ReportsNoOrientationForAnUnknownValue()
    {
        var defaults = WindowsSpoolerCapabilityParser.ReadDefaults(
            WindowsSpoolerCapabilityParser.DmOrientation, 7, 0, 0, 0, 0, [], []);

        Assert.Null(defaults.Orientation);
    }
}
