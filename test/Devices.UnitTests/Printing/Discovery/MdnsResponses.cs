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
            response.AdditionalRecords.Add(new ARecord
            {
                Name = target,
                Address = IPAddress.Parse(address),
                Class = InternetWithCacheFlush,
            });
        }

        return response.ToByteArray();
    }

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
