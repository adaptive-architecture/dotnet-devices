using System.Net;
using Makaretu.Dns;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Builds mDNS responses in the shape a real responder sends: the PTR record in the answer
// section, and the SRV, TXT and address records in the additional section, with the
// cache-flush bit set on every record. The library writes the wire format, including the
// name compression that a real responder uses.
internal static class MdnsResponses
{
    // Multicast DNS uses the top bit of the class field as the cache-flush bit.
    private const DnsClass InternetWithCacheFlush = (DnsClass)0x8001;

    public static byte[] Printer(
        string instance,
        string serviceType,
        string target,
        int port,
        string address,
        IReadOnlyList<string> texts)
    {
        var instanceName = new DomainName($"{instance}.{serviceType}");
        Message response = new() { QR = true, AA = true };
        response.Answers.Add(new PTRRecord
        {
            Name = serviceType,
            DomainName = instanceName,
            Class = InternetWithCacheFlush,
        });
        response.AdditionalRecords.Add(new SRVRecord
        {
            Name = instanceName,
            Port = (ushort)port,
            Target = target,
            Class = InternetWithCacheFlush,
        });

        var text = new TXTRecord { Name = instanceName, Class = InternetWithCacheFlush };
        text.Strings.AddRange(texts);

        response.AdditionalRecords.Add(text);

        if (address is not null)
        {
            var parsed = IPAddress.Parse(address);
            AddressRecord record = parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? new ARecord()
                : new AAAARecord();
            record.Name = target;
            record.Address = parsed;
            record.Class = InternetWithCacheFlush;
            response.AdditionalRecords.Add(record);
        }

        return response.ToByteArray();
    }

    // A response with one answer whose name is a compression pointer to itself: the header
    // is twelve bytes, and the name at offset twelve is "C0 0C", a pointer to offset twelve.
    // No reader can finish such a name, so the packet is malformed.
    public static byte[] SelfPointingName() =>
    [
        0x00, 0x00, // ID
        0x84, 0x00, // QR, AA
        0x00, 0x00, // QDCOUNT
        0x00, 0x01, // ANCOUNT
        0x00, 0x00, // NSCOUNT
        0x00, 0x00, // ARCOUNT
        0xC0, 0x0C, // NAME: pointer to offset 12, this same name
        0x00, 0x0C, // TYPE PTR
        0x00, 0x01, // CLASS IN
        0x00, 0x00, 0x00, 0x78, // TTL
        0x00, 0x00, // RDLENGTH
    ];

    // A response that names an instance but never says where it is.
    public static byte[] PointerOnly(string instance, string serviceType)
    {
        Message response = new() { QR = true, AA = true };
        response.Answers.Add(new PTRRecord
        {
            Name = serviceType,
            DomainName = new DomainName($"{instance}.{serviceType}"),
            Class = InternetWithCacheFlush,
        });
        return response.ToByteArray();
    }
}
