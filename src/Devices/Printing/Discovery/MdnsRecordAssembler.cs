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

        // A printer advertises every protocol that it supports under one service name, so
        // the service name is the key. A key of host and port would report the same printer
        // one time for each protocol. DNS-SD requires a service name to be unique on the
        // link, so the name alone identifies the printer.
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

        printers.Sort(static (left, right) => String.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal));
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
            else if (record is AddressRecord address && address.Address is not null)
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

        // Use the resolved address when the responder sent one. The endpoint then works
        // even if the caller cannot resolve ".local" names.
        var host = addresses.TryGetValue(Key(service.Target), out var address)
            ? address.ToString()
            : service.Target.ToString();

        // The first label of the instance name is the service name, and the first label of
        // the pointer's own name is the service type, for example "_ipp".
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

    // A TXT record holds a list of strings, each one a "key=value" pair. A string with no
    // "=" is a key that has no value.
    private static IReadOnlyDictionary<string, string> ReadAttributes(TXTRecord? text)
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
            if (key.Length > 0)
            {
                attributes[key] = value;
            }
        }

        return attributes;
    }

    private static string GetName(ServiceCandidate candidate, IReadOnlyDictionary<string, string> attributes)
    {
        var model = GetAttribute(attributes, ModelKey);
        if (model is not null)
        {
            return model;
        }

        return candidate.ServiceName.Length > 0 ? candidate.ServiceName : candidate.Host;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && !String.IsNullOrWhiteSpace(value) ? value : null;

    // A printer that offers a raw channel must be reported with that channel, because the
    // raw channel is the only one that TcpPrinterTransport can print to. IPP comes next,
    // because IppPrinterStatusClient can query it. LPD is last, because no transport here
    // writes the LPD message format.
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

        // Ranks decide first. The host and the port break a tie, so that two answers for
        // one instance always give the same result.
        public bool IsBetterThan(ServiceCandidate other)
        {
            if (Rank != other.Rank)
            {
                return Rank < other.Rank;
            }

            var byHost = String.Compare(Host, other.Host, StringComparison.Ordinal);
            return byHost != 0 ? byHost < 0 : Port < other.Port;
        }
    }
}
