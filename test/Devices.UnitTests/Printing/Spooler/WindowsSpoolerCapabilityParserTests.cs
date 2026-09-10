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
}
