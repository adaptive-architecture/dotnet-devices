using AdaptArch.Devices.Printing;
using Makaretu.Dns;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Covers only the thin layer over the DNS library, not the wire format itself.
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

    // A reader of the answer section alone would lose the port and the address.
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

    // Every codec failure must come out as InvalidDataException, whatever it threw.
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
