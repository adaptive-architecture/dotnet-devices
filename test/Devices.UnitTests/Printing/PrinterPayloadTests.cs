using System.Text;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterPayloadTests
{
    [Fact]
    public void FromString_EncodesAsUtf8ByDefault()
    {
        PrinterPayload payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        Assert.Equal(PrinterContentTypes.Zpl, payload.ContentType);
        Assert.Equal("^XA^XZ", Encoding.UTF8.GetString(payload.Data.Span));
    }

    [Fact]
    public void FromString_HonorsExplicitEncoding()
    {
        PrinterPayload payload = PrinterPayload.FromString("ä", PrinterContentTypes.Text, Encoding.Latin1);

        Assert.Equal("ä", Encoding.Latin1.GetString(payload.Data.Span));
    }

    [Fact]
    public void FromBytes_PreservesDataAndContentType()
    {
        byte[] data = [0x1B, 0x40];
        PrinterPayload payload = PrinterPayload.FromBytes(data, PrinterContentTypes.EscPos);

        Assert.Equal(PrinterContentTypes.EscPos, payload.ContentType);
        Assert.Equal(data, payload.Data.ToArray());
    }

    [Fact]
    public void FromString_BlankContentType_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => PrinterPayload.FromString("x", " "));
    }

    [Fact]
    public void ContentTypes_ExposeExpectedConstants()
    {
        Assert.Equal("application/vnd.zebra-zpl", PrinterContentTypes.Zpl);
        Assert.Equal("application/vnd.eltron-epl", PrinterContentTypes.Epl);
        Assert.Equal("application/vnd.zebra-cpcl", PrinterContentTypes.Cpcl);
        Assert.Equal("application/vnd.escpos", PrinterContentTypes.EscPos);
        Assert.Equal("image/png", PrinterContentTypes.Png);
        Assert.Equal("application/pdf", PrinterContentTypes.Pdf);
        Assert.Equal("application/octet-stream", PrinterContentTypes.OctetStream);
    }
}
