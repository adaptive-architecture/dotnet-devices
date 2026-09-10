using AdaptArch.Devices.Printing;
using Makaretu.Dns;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Covers only the thin layer over the DNS library: the unicast-response bit, the two
// sections that must be read, and the translation of a decode failure. The wire format
// itself is the library's responsibility and is not re-tested here.
public class MdnsMessagesTests
{
    [Fact]
    public void CreatePtrQuery_AsksForPtrWithUnicastResponseBit()
    {
        var query = MdnsMessages.CreatePtrQuery(MdnsPrinterDiscoveryOptions.IppServiceType);

        var parsed = (Message)new Message().Read(query);
        var question = Assert.Single(parsed.Questions);
        Assert.Equal(DnsType.PTR, question.Type);
        Assert.Equal("_ipp._tcp.local", question.Name.ToString());

        // The top bit of the class field asks the responder to answer by unicast.
        Assert.Equal(0x8001, (int)question.Class);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePtrQuery_BlankServiceType_Throws(string serviceType) =>
        Assert.ThrowsAny<ArgumentException>(() => MdnsMessages.CreatePtrQuery(serviceType));

    // A responder puts the SRV, TXT and address records in the additional section, so a
    // reader that took only the answer section would lose the port and the address.
    [Fact]
    public void ReadRecords_ReturnsAnswerAndAdditionalSections()
    {
        var payload = MdnsResponses.Printer(
            instance: "Front Desk",
            serviceType: MdnsPrinterDiscoveryOptions.IppServiceType,
            target: "desk.local",
            port: 631,
            address: "192.168.1.51",
            texts: ["ty=Model X"]);

        var records = MdnsMessages.ReadRecords(payload);

        Assert.Single(records.OfType<PTRRecord>());
        Assert.Single(records.OfType<SRVRecord>());
        Assert.Single(records.OfType<TXTRecord>());
        Assert.Single(records.OfType<ARecord>());
    }

    [Fact]
    public void ReadRecords_TruncatedPayload_ThrowsInvalidData()
    {
        var payload = MdnsResponses.Printer(
            "Front Desk", MdnsPrinterDiscoveryOptions.IppServiceType, "desk.local", 631, "192.168.1.51", []);

        Assert.Throws<InvalidDataException>(() => MdnsMessages.ReadRecords(payload[..(payload.Length - 4)]));
    }

    // The codec reports a compression pointer to a name it has not read yet with an
    // exception type that no list of "parse" exceptions would guess. Every failure of the
    // codec must still come out as InvalidDataException, so the browse can skip the packet.
    [Fact]
    public void ReadRecords_SelfPointingName_ThrowsInvalidData() =>
        Assert.Throws<InvalidDataException>(() => MdnsMessages.ReadRecords(MdnsResponses.SelfPointingName()));

    [Fact]
    public void ReadRecords_EmptyPayload_ThrowsInvalidData() =>
        Assert.Throws<InvalidDataException>(() => MdnsMessages.ReadRecords([]));

    [Fact]
    public void ReadRecords_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => MdnsMessages.ReadRecords(null));
}
