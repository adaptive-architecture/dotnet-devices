using System.Text;
using DotNetSnmp.Asn1.Serialization;
using DotNetSnmp.Asn1.SyntaxObjects;
using Lextm.SharpSnmpLib;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reads the values that an SNMP agent returns. It turns the data types of
/// <see cref="IAsnSerializable"/> into the numbers, text and bytes that the Printer MIB rules use.
/// </summary>
internal static class SnmpValues
{
    /// <summary>
    /// Reports whether the agent returned no value. A printer that supplies only a part of
    /// the Printer MIB answers this way, which is not an error.
    /// </summary>
    /// <param name="data">The value from the agent.</param>
    /// <returns><c>true</c> when there is no value.</returns>
    public static bool IsAbsent(IAsnSerializable data) =>
        data is null ||
        data.TypeCode == SnmpType.Null ||
        data.TypeCode == SnmpType.NoSuchObject ||
        data.TypeCode == SnmpType.NoSuchInstance ||
        data.TypeCode == SnmpType.EndOfMibView;

    /// <summary>
    /// Reports whether the walk has passed the last row of a column.
    /// </summary>
    /// <param name="data">The value from the agent.</param>
    /// <returns><c>true</c> when the walk is finished.</returns>
    public static bool IsEndOfView(IAsnSerializable data) => data is not null && data.TypeCode == SnmpType.EndOfMibView;

    /// <summary>
    /// Reads a whole number. A counter that is larger than a signed 64-bit number reports
    /// <c>null</c> instead of failing the whole query.
    /// </summary>
    /// <param name="data">The value from the agent.</param>
    /// <returns>The number, or <c>null</c> when the value is absent or is not a number.</returns>
    public static long? GetNumber(IAsnSerializable data)
    {
        if (data is Integer32 integer)
        {
            return integer.Value;
        }

        if (data is Counter32 counter)
        {
            return counter.Value;
        }

        if (data is Gauge32 gauge)
        {
            return gauge.Value;
        }

        if (data is TimeTicks ticks)
        {
            return ticks.Value;
        }

        if (data is Counter64 counter64)
        {
            var value = counter64.Value;
            return value > Int64.MaxValue ? null : (long)value;
        }

        return null;
    }

    /// <summary>
    /// Reads a text value. Leading and trailing white space and null bytes are removed,
    /// because printers pad these fields.
    /// </summary>
    /// <param name="data">The value from the agent.</param>
    /// <returns>The text, or <c>null</c> when the value is absent or empty.</returns>
    public static string? GetText(IAsnSerializable data)
    {
        if (IsAbsent(data))
        {
            return null;
        }

        var text = data is OctetString octets
            ? Encoding.UTF8.GetString(octets.Octets)
            : data.ToString() ?? String.Empty;
        text = text.Trim().Trim('\0').Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Reads the raw bytes of an octet string, for a value that is a bit field rather than
    /// text.
    /// </summary>
    /// <param name="data">The value from the agent.</param>
    /// <returns>The bytes, or <c>null</c> when the value is not an octet string.</returns>
    public static byte[]? GetBytes(IAsnSerializable data) => data is OctetString octets ? octets.Octets : null;
}
