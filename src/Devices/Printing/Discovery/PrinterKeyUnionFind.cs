namespace AdaptArch.Devices.Printing;

// Union by size with path compression, over keys rather than channels: a key is what two
// channels have in common, and a key can be named by a channel that is not there.
//
// PrinterDeviceGrouper and PrinterQueueCorrelator both need the same provisional grouping,
// so they share this rather than keeping two copies that could drift.
internal sealed class PrinterKeyUnionFind
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
    /// The strongest key of the set a representative stands for: an identity the device
    /// reported beats an address, and a UUID beats a serial number.
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

    /// <summary>
    /// Seeds a set from the channels a discovery reported: every channel vouched for its
    /// own key and each of its aliases together, so they all join one set.
    /// </summary>
    public static PrinterKeyUnionFind Seed(IEnumerable<DiscoveredPrinter> channels)
    {
        PrinterKeyUnionFind sets = new();
        foreach (var channel in channels)
        {
            var own = channel.Id.DeviceKey;
            sets.Add(own);
            foreach (var alias in channel.Aliases)
            {
                sets.Add(alias);
                sets.Union(own, alias);
            }
        }

        return sets;
    }
}
