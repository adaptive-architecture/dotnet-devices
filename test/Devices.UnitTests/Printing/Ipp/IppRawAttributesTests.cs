using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppRawAttributesTests
{
    [Fact]
    public async Task ReadJobText_ReadsAnAttributeTheTypedModelDoesNotCarry()
    {
        var body = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 7),
            (0x23, "job-state", 6),
            (0x41, "job-printer-state-message", "The printer is not responding."));
        var (_, raw) = await IppMessages.DecodeWithRawAsync(body);

        Assert.Equal("The printer is not responding.", IppRawAttributes.ReadJobText(raw, 0, "job-printer-state-message"));
    }

    [Fact]
    public async Task ReadJobText_ReturnsNullForAnAttributeThatIsNotThere()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 6));
        var (_, raw) = await IppMessages.DecodeWithRawAsync(body);

        Assert.Null(IppRawAttributes.ReadJobText(raw, 0, "job-printer-state-message"));
    }

    [Fact]
    public async Task ReadJobText_ReturnsNullForAnIndexOutsideTheGroups()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 6));
        var (_, raw) = await IppMessages.DecodeWithRawAsync(body);

        Assert.Null(IppRawAttributes.ReadJobText(raw, 9, "job-printer-state-message"));
        Assert.Null(IppRawAttributes.ReadJobText(null, 0, "job-printer-state-message"));
    }
}
