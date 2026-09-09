using Makaretu.Dns;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Writes and reads the DNS packets that multicast DNS service discovery uses, with
/// <c>Makaretu.Dns.New</c>.
/// </summary>
/// <remarks>
/// Only the wire codec of that library is used. The datagrams travel over
/// <see cref="IUdpChannel"/>, so the browse keeps its own socket strategy: an ephemeral
/// source port, which RFC 6762 §6.7 requires a responder to answer by unicast.
/// </remarks>
internal static class MdnsMessages
{
    /// <summary>
    /// The DNS class for the internet, with the top bit set to ask for a unicast answer.
    /// </summary>
    /// <remarks>
    /// Multicast DNS defines the top bit of the class field as the unicast-response bit.
    /// <see cref="DnsClass"/> has no name for it, so the value is cast.
    /// </remarks>
    private const DnsClass InternetWithUnicastResponse = (DnsClass)0x8001;

    /// <summary>
    /// Writes a PTR query for one DNS-SD service type.
    /// </summary>
    /// <param name="serviceType">The service type, for example <c>_ipp._tcp.local</c>.</param>
    /// <returns>The bytes of the query.</returns>
    public static byte[] CreatePtrQuery(string serviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);

        Message query = new();
        query.Questions.Add(new Question
        {
            Name = serviceType,
            Type = DnsType.PTR,
            Class = InternetWithUnicastResponse,
        });
        return query.ToByteArray();
    }

    /// <summary>
    /// Reads the records of a DNS response, from both the answer section and the
    /// additional section. A responder puts the SRV, TXT and address records in the
    /// additional section, so both are needed.
    /// </summary>
    /// <param name="payload">The bytes of the response.</param>
    /// <returns>The records of the response.</returns>
    /// <exception cref="InvalidDataException">Thrown when the packet is malformed or ends too soon.</exception>
    public static IReadOnlyList<ResourceRecord> ReadRecords(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Message message;
        try
        {
            message = (Message)new Message().Read(payload);
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or FormatException or
                         ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The multicast DNS response is malformed.", exception);
        }

        List<ResourceRecord> records = new(message.Answers.Count + message.AdditionalRecords.Count);
        records.AddRange(message.Answers);
        records.AddRange(message.AdditionalRecords);
        return records;
    }
}
