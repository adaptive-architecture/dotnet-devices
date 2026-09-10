using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Turns the records of one or more multicast DNS responses into printers.
/// This type holds the join and the de-duplication rules, and it touches no socket,
/// so it can be tested on its own.
/// </summary>
internal static class MdnsRecordAssembler
{
    /// <summary>
    /// The TXT key that holds the make and the model of the printer.
    /// </summary>
    private const string ModelKey = "ty";

    /// <summary>
    /// The TXT key that holds a note about the place of the printer.
    /// </summary>
    private const string NoteKey = "note";

    /// <summary>
    /// The TXT key that lists the page description languages of the printer.
    /// </summary>
    private const string PageDescriptionLanguageKey = "pdl";

    /// <summary>
    /// Builds the printers that a set of DNS records describes.
    /// </summary>
    /// <param name="records">The records from every response that the browse received.</param>
    /// <returns>One printer for each service instance, sorted by identifier.</returns>
    public static IReadOnlyList<DiscoveredPrinter> Assemble(IReadOnlyList<ResourceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        Dictionary<string, SRVRecord> services = new(StringComparer.Ordinal);
        Dictionary<string, TXTRecord> texts = new(StringComparer.Ordinal);
        Dictionary<string, IPAddress> addresses = new(StringComparer.Ordinal);
        List<PTRRecord> pointers = [];
        Index(records, services, texts, addresses, pointers);

        // One printer advertises every protocol under one service name, which DNS-SD
        // keeps unique on the link, so that name alone identifies the printer.
        Dictionary<string, ServiceCandidate> best = new(StringComparer.OrdinalIgnoreCase);
        foreach (var pointer in pointers)
        {
            AddCandidate(best, pointer, services, addresses);
        }

        List<DiscoveredPrinter> printers = new(best.Count);
        foreach (var candidate in best.Values)
        {
            printers.Add(CreatePrinter(candidate, texts));
        }

        printers.Sort(static (left, right) => String.CompareOrdinal(left.Id.Value, right.Id.Value));
        return printers;
    }

    private static void Index(
        IReadOnlyList<ResourceRecord> records,
        Dictionary<string, SRVRecord> services,
        Dictionary<string, TXTRecord> texts,
        Dictionary<string, IPAddress> addresses,
        List<PTRRecord> pointers)
    {
        foreach (var record in records)
        {
            if (record is PTRRecord pointer && pointer.DomainName is not null)
            {
                pointers.Add(pointer);
            }
            else if (record is SRVRecord service && service.Target is not null)
            {
                services.TryAdd(Key(record.Name), service);
            }
            else if (record is TXTRecord text)
            {
                texts.TryAdd(Key(record.Name), text);
            }
            else if (record is AddressRecord address && IsReachable(address.Address))
            {
                // Prefer IPv4, because the endpoint is handed to a TCP transport.
                var key = Key(record.Name);
                if (address.Address.AddressFamily == AddressFamily.InterNetwork || !addresses.ContainsKey(key))
                {
                    addresses[key] = address.Address;
                }
            }
        }
    }

    // An address no other host can connect to must not replace the resolvable name.
    private static bool IsReachable(IPAddress? address) =>
        address is not null &&
        !IPAddress.IsLoopback(address) &&
        !address.Equals(IPAddress.Any) &&
        !address.Equals(IPAddress.IPv6Any) &&
        !IsMulticast(address);

    private static bool IsMulticast(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
            ? address.GetAddressBytes()[0] is >= 224 and <= 239
            : address.IsIPv6Multicast;

    private static void AddCandidate(
        Dictionary<string, ServiceCandidate> best,
        PTRRecord pointer,
        Dictionary<string, SRVRecord> services,
        Dictionary<string, IPAddress> addresses)
    {
        var instance = pointer.DomainName;
        if (!services.TryGetValue(Key(instance), out var service))
        {
            // Without an SRV record there is no port, so the printer is not addressable.
            return;
        }

        // The resolved address also works for a caller that cannot resolve ".local".
        var host = addresses.TryGetValue(Key(service.Target), out var address)
            ? address.ToString()
            : service.Target.ToString();

        // First label of the instance is the service name, of the pointer the type ("_ipp").
        var serviceName = FirstLabel(instance);
        ServiceCandidate candidate = new(
            Key(instance), serviceName, host, service.Port, GetRank(FirstLabel(pointer.Name)));
        if (!best.TryGetValue(serviceName, out var current) || candidate.IsBetterThan(current))
        {
            best[serviceName] = candidate;
        }
    }

    private static DiscoveredPrinter CreatePrinter(ServiceCandidate candidate, Dictionary<string, TXTRecord> texts)
    {
        _ = texts.TryGetValue(candidate.InstanceKey, out var text);
        var attributes = ReadAttributes(text);

        var id = PrinterId.FromNetwork(candidate.Host);
        NetworkPrinterEndpoint endpoint = new(candidate.Host, candidate.Port);
        PrinterInfo info = new(id, GetName(candidate, attributes))
        {
            Location = GetAttribute(attributes, NoteKey),
            DriverName = GetAttribute(attributes, PageDescriptionLanguageKey),
        };
        return new DiscoveredPrinter(id, endpoint, info) { Source = DiscoverySource.Mdns };
    }

    // Each TXT string is "key=value". A string with no "=" is a key with no value.
    private static Dictionary<string, string> ReadAttributes(TXTRecord? text)
    {
        Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
        if (text is null)
        {
            return attributes;
        }

        foreach (var entry in text.Strings)
        {
            if (String.IsNullOrEmpty(entry))
            {
                continue;
            }

            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? entry : entry[..separator];
            var value = separator < 0 ? String.Empty : entry[(separator + 1)..];
            // RFC 6763 §6.4: when a key appears more than one time, the first one counts.
            if (key.Length > 0)
            {
                _ = attributes.TryAdd(key, value);
            }
        }

        return attributes;
    }

    private static string GetName(ServiceCandidate candidate, IReadOnlyDictionary<string, string> attributes)
    {
        var model = GetAttribute(attributes, ModelKey);
        return model ?? (candidate.ServiceName.Length > 0 ? candidate.ServiceName : candidate.Host);
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && !String.IsNullOrWhiteSpace(value) ? value : null;

    // Raw first: it is the only channel TcpPrinterTransport can print to. Then IPP,
    // which can at least be queried. LPD last: no transport here writes its format.
    private static int GetRank(string serviceTypeLabel)
    {
        if (String.Equals(serviceTypeLabel, "_pdl-datastream", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (String.Equals(serviceTypeLabel, "_ipp", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(serviceTypeLabel, "_ipps", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (String.Equals(serviceTypeLabel, "_printer", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    /// <summary>
    /// Builds a dictionary key for a name. DNS names do not depend on case, so the
    /// canonical form is used.
    /// </summary>
    private static string Key(DomainName name) => name.ToCanonical().ToString();

    /// <summary>
    /// Gets the first label of a name, without the escaping that a full name string uses.
    /// </summary>
    private static string FirstLabel(DomainName name) => name.Labels.Count > 0 ? name.Labels[0] : String.Empty;

    private sealed class ServiceCandidate
    {
        public ServiceCandidate(string instanceKey, string serviceName, string host, int port, int rank)
        {
            InstanceKey = instanceKey;
            ServiceName = serviceName;
            Host = host;
            Port = port;
            Rank = rank;
        }

        public string InstanceKey { get; }

        public string ServiceName { get; }

        public string Host { get; }

        public int Port { get; }

        public int Rank { get; }

        // The host and the port break a tie, so the result is stable.
        public bool IsBetterThan(ServiceCandidate other)
        {
            if (Rank != other.Rank)
            {
                return Rank < other.Rank;
            }

            var byHost = String.CompareOrdinal(Host, other.Host);
            return byHost != 0 ? byHost < 0 : Port < other.Port;
        }
    }
}
