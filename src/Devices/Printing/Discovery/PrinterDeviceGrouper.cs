namespace AdaptArch.Devices.Printing;

// Puts the channels that reach one physical device into one PrinterDevice.
//
// The rule is evidence, never resemblance. Two channels are joined when a discovery
// source said they name the same device: a multicast DNS UUID record, an IPP
// printer-uuid, an SNMP serial number, or the device URI of a print queue. Two printers
// of the same model, in the same room, with the same name are two devices, and this type
// keeps them apart.
internal static class PrinterDeviceGrouper
{
    /// <summary>
    /// Groups channels into devices.
    /// </summary>
    /// <param name="channels">The channels every discovery source reported.</param>
    /// <param name="statusSources">The read-only protocols that answered, by channel identifier.</param>
    /// <returns>The devices, ordered by key.</returns>
    public static IReadOnlyList<PrinterDevice> Group(
        IReadOnlyList<DiscoveredPrinter> channels,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterStatusSource>>? statusSources = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
        {
            return [];
        }

        // Sorted first, so union by size breaks its ties the same way on every run and
        // the same input always produces the same keys.
        List<DiscoveredPrinter> ordered = [.. channels];
        ordered.Sort(static (left, right) => String.CompareOrdinal(left.Id.ToString(), right.Id.ToString()));

        UnionFind sets = new();
        foreach (var channel in ordered)
        {
            var own = channel.Id.DeviceKey;
            sets.Add(own);
            foreach (var alias in channel.Aliases)
            {
                // The channel vouched for its own key and every alias together, so they
                // all join one set, and the aliases join each other through it.
                sets.Add(alias);
                sets.Union(own, alias);
            }
        }

        Dictionary<PrinterDeviceKey, List<DiscoveredPrinter>> grouped = [];
        foreach (var channel in ordered)
        {
            var representative = sets.Find(channel.Id.DeviceKey);
            if (grouped.TryGetValue(representative, out var members))
            {
                members.Add(channel);
            }
            else
            {
                grouped[representative] = [channel];
            }
        }

        List<PrinterDevice> devices = new(grouped.Count);
        foreach ((var representative, var members) in grouped)
        {
            // The strongest key in the whole set names the device, not whichever member
            // the union happened to settle on.
            var key = sets.Best(representative);
            devices.Add(new PrinterDevice(key, members, ReadSources(members, statusSources)));
        }

        devices.Sort(static (left, right) => String.CompareOrdinal(left.Key.ToString(), right.Key.ToString()));
        return devices;
    }

    private static List<PrinterStatusSource> ReadSources(
        List<DiscoveredPrinter> members,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterStatusSource>>? statusSources)
    {
        if (statusSources is null)
        {
            return [];
        }

        List<PrinterStatusSource> found = [];
        foreach (var member in members)
        {
            if (!statusSources.TryGetValue(member.Id, out var sources))
            {
                continue;
            }

            found.AddRange(sources.Where(source => !found.Contains(source)));
        }

        found.Sort();
        return found;
    }

    // Union by size with path compression, over keys rather than channels: a key is what
    // two channels have in common, and a key can be named by a channel that is not there.
    private sealed class UnionFind
    {
        private readonly Dictionary<PrinterDeviceKey, PrinterDeviceKey> _parent = [];
        private readonly Dictionary<PrinterDeviceKey, int> _size = [];

        public void Add(PrinterDeviceKey key)
        {
            if (_parent.TryAdd(key, key))
            {
                _size[key] = 1;
            }
        }

        public PrinterDeviceKey Find(PrinterDeviceKey key)
        {
            var root = key;
            while (!_parent[root].Equals(root))
            {
                root = _parent[root];
            }

            while (!_parent[key].Equals(root))
            {
                var next = _parent[key];
                _parent[key] = root;
                key = next;
            }

            return root;
        }

        public void Union(PrinterDeviceKey left, PrinterDeviceKey right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a.Equals(b))
            {
                return;
            }

            if (_size[a] < _size[b])
            {
                (a, b) = (b, a);
            }

            _parent[b] = a;
            _size[a] += _size[b];
        }

        /// <summary>
        /// The strongest key of the set a representative stands for: an identity the
        /// device reported beats an address, and a UUID beats a serial number.
        /// </summary>
        public PrinterDeviceKey Best(PrinterDeviceKey representative)
        {
            var best = representative;
            var bestRank = Int32.MaxValue;
            foreach (var key in _parent.Keys)
            {
                if (!Find(key).Equals(representative))
                {
                    continue;
                }

                var rank = PrinterDeviceKey.Rank(key);

                // The value breaks a tie, so the key never depends on dictionary order.
                if (rank < bestRank || (rank == bestRank && String.CompareOrdinal(key.Value, best.Value) < 0))
                {
                    best = key;
                    bestRank = rank;
                }
            }

            return best;
        }
    }
}
