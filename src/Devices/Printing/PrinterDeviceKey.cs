namespace AdaptArch.Devices.Printing;

/// <summary>
/// Groups the channels that reach one physical device. Two channels belong to the same
/// <see cref="PrinterDevice"/> when their keys are equal, or when a discovery source
/// vouched that they name the same device.
/// </summary>
public readonly struct PrinterDeviceKey : IEquatable<PrinterDeviceKey>
{
    // A device that reports one of these reports nothing. A whole fleet can share such a
    // value, and merging on it would collapse the fleet into a single device, which is
    // the worst failure this type can have.
    private static readonly string[] UselessIdentities =
    [
        "n/a", "na", "none", "null", "nil", "0", "unknown", "unspecified", "default",
        "sn", "sn:", "serial", "00000000-0000-0000-0000-000000000000",
    ];

    private PrinterDeviceKey(PrinterDeviceKeyKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets what kind of value the key holds.
    /// </summary>
    public PrinterDeviceKeyKind Kind { get; }

    /// <summary>
    /// Gets the key value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets a value indicating whether the device reported this identity about itself, so
    /// that the key survives a change of address.
    /// </summary>
    public bool IsDeviceIdentity => Kind == PrinterDeviceKeyKind.DeviceIdentity;

    /// <summary>
    /// Creates a key from an identity the device reported: a UUID or a serial number.
    /// </summary>
    /// <param name="value">The reported identity.</param>
    /// <exception cref="ArgumentException">Thrown when the value is one no device is identified by, such as a blank string or an empty UUID. Test with <see cref="IsUsableIdentity"/> first.</exception>
    public static PrinterDeviceKey ForDeviceIdentity(string value)
    {
        if (!IsUsableIdentity(value))
        {
            throw new ArgumentException($"'{value}' does not identify a device.", nameof(value));
        }

        return new PrinterDeviceKey(PrinterDeviceKeyKind.DeviceIdentity, value.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Creates a key from a host name or an IP address.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    public static PrinterDeviceKey ForHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return new PrinterDeviceKey(PrinterDeviceKeyKind.Host, host);
    }

    /// <summary>
    /// Creates a key from the name of a print queue of the operating system spooler.
    /// </summary>
    /// <param name="name">The queue name.</param>
    public static PrinterDeviceKey ForQueue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new PrinterDeviceKey(PrinterDeviceKeyKind.Queue, name);
    }

    /// <summary>
    /// Tells whether a reported identity names a device at all.
    /// </summary>
    /// <remarks>
    /// A printer that has no serial number configured often reports a placeholder rather
    /// than nothing. Grouping on such a value would fuse every printer that shares it.
    /// </remarks>
    /// <param name="value">The reported identity, which may be <c>null</c>.</param>
    /// <returns><c>true</c> when the value can identify one device.</returns>
    public static bool IsUsableIdentity(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Two characters cannot tell one device of a fleet from another.
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (UselessIdentities.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // A value made only of the same repeated character, such as "0000" or "-----",
        // is a placeholder rather than an identity.
        return trimmed.AsSpan().ContainsAnyExcept(trimmed[0]);
    }

    /// <summary>
    /// The order in which a key is preferred as the key of a device. A lower number wins,
    /// so an identity the device reported always beats an address.
    /// </summary>
    internal static int Rank(PrinterDeviceKey key)
    {
        if (key.Kind == PrinterDeviceKeyKind.DeviceIdentity)
        {
            // A UUID is a stronger statement than a serial number, which a vendor may
            // reuse across product lines.
            return Guid.TryParseExact(key.Value, "D", out _) ? 0 : 1;
        }

        if (key.Kind == PrinterDeviceKeyKind.Host)
        {
            return 2;
        }

        return Int32.MaxValue;
    }

    private StringComparison Comparison =>
        Kind is PrinterDeviceKeyKind.Host or PrinterDeviceKeyKind.Queue
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <inheritdoc />
    public bool Equals(PrinterDeviceKey other) =>
        Kind == other.Kind && String.Equals(Value, other.Value, Comparison);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrinterDeviceKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var comparer = Kind is PrinterDeviceKeyKind.Host or PrinterDeviceKeyKind.Queue
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return HashCode.Combine((int)Kind, Value is null ? 0 : comparer.GetHashCode(Value));
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{Value}";

    /// <summary>
    /// Compares two keys for equality.
    /// </summary>
    public static bool operator ==(PrinterDeviceKey left, PrinterDeviceKey right) => left.Equals(right);

    /// <summary>
    /// Compares two keys for inequality.
    /// </summary>
    public static bool operator !=(PrinterDeviceKey left, PrinterDeviceKey right) => !left.Equals(right);
}
