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
    /// <param name="formats">The formats each device answers about, or <c>null</c> for the default policy.</param>
    /// <returns>The devices, ordered by key.</returns>
    public static IReadOnlyList<PrinterDevice> Group(
        IReadOnlyList<DiscoveredPrinter> channels,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterStatusSource>>? statusSources = null,
        PrintFormatPolicy? formats = null)
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

        // The channel vouched for its own key and every alias together, so they all join
        // one set, and the aliases join each other through it.
        var sets = PrinterKeyUnionFind.Seed(ordered);

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
            devices.Add(new PrinterDevice(key, members, ReadSources(members, statusSources))
            {
                Formats = formats ?? PrintFormatPolicy.Default,
            });
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
}
