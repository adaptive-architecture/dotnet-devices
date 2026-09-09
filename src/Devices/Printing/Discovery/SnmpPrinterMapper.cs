using DotNetSnmp.Asn1.Serialization;
using DotNetSnmp.Asn1.SyntaxObjects;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Turns SNMP variable bindings into the shared printer models. This type holds the
/// Printer MIB rules and touches no socket, so it can be tested on its own.
/// </summary>
internal static class SnmpPrinterMapper
{
    /// <summary>
    /// The bit names of <c>hrPrinterDetectedErrorState</c>, in the order that the Host
    /// Resources MIB defines. Bit 0 is the highest bit of the first byte.
    /// </summary>
    private static readonly string[] ErrorBitNames =
    [
        "lowPaper",
        "noPaper",
        "lowToner",
        "noToner",
        "doorOpen",
        "jammed",
        "offline",
        "serviceRequested",
        "inputTrayMissing",
        "outputTrayMissing",
        "markerSupplyMissing",
        "outputNearFull",
        "outputFull",
        "inputTrayEmpty",
        "overduePreventMaint",
    ];

    /// <summary>
    /// The bits that stop the printer. A supply that is only low does not appear here,
    /// because a printer with a low level of toner can still print.
    /// </summary>
    private static readonly int[] ErrorBits = [1, 3, 4, 5, 7, 8, 9, 10, 12];

    /// <summary>
    /// The bit that reports the printer as offline.
    /// </summary>
    private const int OfflineBit = 6;

    /// <summary>
    /// Builds the details of a printer.
    /// </summary>
    /// <param name="host">The host that was queried.</param>
    /// <param name="scalars">The variable bindings of the scalar GET.</param>
    /// <param name="supplies">The variable bindings of the supply table walk.</param>
    /// <returns>The details.</returns>
    public static SnmpPrinterDetails Map(
        string host,
        IReadOnlyList<Variable> scalars,
        IReadOnlyList<Variable> supplies)
    {
        Dictionary<string, IAsnSerializable> values = new(StringComparer.Ordinal);
        foreach (var variable in scalars)
        {
            if (!SnmpValues.IsAbsent(variable.Data))
            {
                values.TryAdd(variable.Id.Oid, variable.Data);
            }
        }

        var id = PrinterId.FromNetwork(host);
        var name = GetText(values, PrinterMibOids.PrinterName)
            ?? GetText(values, PrinterMibOids.SystemName)
            ?? GetText(values, PrinterMibOids.SystemDescription)
            ?? host;

        PrinterInfo info = new(id, name)
        {
            Location = GetText(values, PrinterMibOids.SystemLocation),
        };

        var status = CreateStatus(id, values, supplies);
        return new SnmpPrinterDetails(info, status)
        {
            SerialNumber = GetText(values, PrinterMibOids.SerialNumber),
            LifetimePageCount = GetNumber(values, PrinterMibOids.MarkerLifeCount),
        };
    }

    private static PrinterStatus CreateStatus(
        PrinterId id,
        Dictionary<string, IAsnSerializable> values,
        IReadOnlyList<Variable> supplies)
    {
        var state = MapState(GetNumber(values, PrinterMibOids.PrinterStatus));
        List<string> reasons = [];
        if (values.TryGetValue(PrinterMibOids.DetectedErrorState, out var errors) &&
            SnmpValues.GetBytes(errors) is byte[] bits)
        {
            state = ApplyErrorBits(bits, state, reasons);
        }

        return new PrinterStatus(id, state)
        {
            IsAcceptingJobs = state != PrinterStatusState.Error && state != PrinterStatusState.Offline,
            Detail = reasons.Count == 0 ? null : String.Join("; ", reasons),
            Markers = MapMarkers(supplies),
        };
    }

    private static PrinterStatusState ApplyErrorBits(byte[] bits, PrinterStatusState state, List<string> reasons)
    {
        var offline = false;
        var error = false;
        for (var bit = 0; bit < ErrorBitNames.Length; bit++)
        {
            if (!IsSet(bits, bit))
            {
                continue;
            }

            // Every set bit is reported, so that a warning stays visible to the caller.
            reasons.Add(ErrorBitNames[bit]);
            if (bit == OfflineBit)
            {
                offline = true;
            }
            else if (Array.IndexOf(ErrorBits, bit) >= 0)
            {
                error = true;
            }
        }

        if (offline)
        {
            return PrinterStatusState.Offline;
        }

        return error ? PrinterStatusState.Error : state;
    }

    private static bool IsSet(byte[] bits, int bit)
    {
        var index = bit / 8;
        return index < bits.Length && (bits[index] & (0x80 >> (bit % 8))) != 0;
    }

    private static IReadOnlyList<PrinterMarker> MapMarkers(IReadOnlyList<Variable> supplies)
    {
        List<string> order = [];
        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        Dictionary<string, long> capacities = new(StringComparer.Ordinal);
        Dictionary<string, long> levels = new(StringComparer.Ordinal);
        Dictionary<string, long> colorantIndexes = new(StringComparer.Ordinal);
        Dictionary<string, string> colorants = new(StringComparer.Ordinal);

        foreach (var variable in supplies)
        {
            if (SnmpValues.IsAbsent(variable.Data))
            {
                continue;
            }

            CollectSupplyValue(variable, order, descriptions, capacities, levels, colorantIndexes, colorants);
        }

        List<PrinterMarker> markers = new(order.Count);
        foreach (var row in order)
        {
            markers.Add(CreateMarker(row, descriptions, capacities, levels, colorantIndexes, colorants));
        }

        return markers;
    }

    private static void CollectSupplyValue(
        Variable variable,
        List<string> order,
        Dictionary<string, string> descriptions,
        Dictionary<string, long> capacities,
        Dictionary<string, long> levels,
        Dictionary<string, long> colorantIndexes,
        Dictionary<string, string> colorants)
    {
        var oid = variable.Id.Oid;
        var number = SnmpValues.GetNumber(variable.Data);

        var row = GetRow(oid, PrinterMibOids.SuppliesDescription);
        if (row is not null)
        {
            var text = SnmpValues.GetText(variable.Data);
            if (text is not null)
            {
                descriptions[row] = text;
            }

            if (!order.Contains(row))
            {
                order.Add(row);
            }

            return;
        }

        row = GetRow(oid, PrinterMibOids.SuppliesMaxCapacity);
        if (row is not null && number is not null)
        {
            capacities[row] = number.Value;
            return;
        }

        row = GetRow(oid, PrinterMibOids.SuppliesLevel);
        if (row is not null && number is not null)
        {
            levels[row] = number.Value;
            if (!order.Contains(row))
            {
                order.Add(row);
            }

            return;
        }

        row = GetRow(oid, PrinterMibOids.SuppliesColorantIndex);
        if (row is not null && number is not null)
        {
            colorantIndexes[row] = number.Value;
            return;
        }

        row = GetRow(oid, PrinterMibOids.ColorantValue);
        if (row is not null)
        {
            var text = SnmpValues.GetText(variable.Data);
            if (text is not null)
            {
                colorants[row] = text;
            }
        }
    }

    private static PrinterMarker CreateMarker(
        string row,
        Dictionary<string, string> descriptions,
        Dictionary<string, long> capacities,
        Dictionary<string, long> levels,
        Dictionary<string, long> colorantIndexes,
        Dictionary<string, string> colorants)
    {
        var name = descriptions.TryGetValue(row, out var description) ? description : $"Supply {row}";
        PrinterMarker marker = new(name);

        if (levels.TryGetValue(row, out var level) && capacities.TryGetValue(row, out var capacity))
        {
            marker.LevelPercent = GetLevelPercent(level, capacity);
        }

        // prtMarkerSuppliesColorantIndex names the colorant row. The device index is the
        // first sub-identifier of the supply row, and the colorant index replaces the second.
        if (colorantIndexes.TryGetValue(row, out var colorantIndex))
        {
            var separator = row.IndexOf('.', StringComparison.Ordinal);
            var device = separator < 0 ? row : row[..separator];
            if (colorants.TryGetValue($"{device}.{colorantIndex}", out var color))
            {
                marker.Color = color;
            }
        }

        return marker;
    }

    // The Printer MIB uses negative levels to say that a number is not available:
    // -1 places no restriction, -2 is unknown, and -3 means some supply remains but the
    // amount is indeterminate. None of them is a quantity.
    private static int? GetLevelPercent(long level, long capacity)
    {
        if (level < 0 || capacity <= 0)
        {
            return null;
        }

        var percent = level * 100 / capacity;
        return (int)Math.Clamp(percent, 0, 100);
    }

    private static PrinterStatusState MapState(long? state)
    {
        if (state == 3)
        {
            return PrinterStatusState.Idle;
        }

        // A printer that is printing and a printer that is warming up are both busy.
        if (state == 4 || state == 5)
        {
            return PrinterStatusState.Processing;
        }

        return PrinterStatusState.Unknown;
    }

    private static string? GetRow(string oid, string column) =>
        oid.Length > column.Length + 1 && oid.StartsWith(column, StringComparison.Ordinal) && oid[column.Length] == '.'
            ? oid[(column.Length + 1)..]
            : null;

    private static string? GetText(Dictionary<string, IAsnSerializable> values, string oid) =>
        values.TryGetValue(oid, out var data) ? SnmpValues.GetText(data) : null;

    private static long? GetNumber(Dictionary<string, IAsnSerializable> values, string oid) =>
        values.TryGetValue(oid, out var data) ? SnmpValues.GetNumber(data) : null;
}
