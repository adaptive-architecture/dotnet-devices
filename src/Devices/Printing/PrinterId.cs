namespace AdaptArch.Devices.Printing;

/// <summary>
/// Stable identifier of a printer.
/// </summary>
public readonly struct PrinterId : IEquatable<PrinterId>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterId"/> struct.
    /// </summary>
    /// <param name="kind">The addressing scheme of the printer.</param>
    /// <param name="value">The identifier value. For spooler printers this is the queue name; for network printers a host or URI; for USB printers a device path or serial number.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty, or when <paramref name="kind"/> is <see cref="PrinterIdKind.Network"/> and <paramref name="value"/> is not a valid host name or address.</exception>
    public PrinterId(PrinterIdKind kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (kind == PrinterIdKind.Network)
        {
            NetworkPrinterEndpoint.ThrowIfNotAHost(value);
        }

        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets the addressing scheme of the printer.
    /// </summary>
    public PrinterIdKind Kind { get; }

    /// <summary>
    /// Gets the identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a spooler printer identifier from an operating system queue name.
    /// </summary>
    /// <param name="queueName">The operating system print queue name.</param>
    /// <returns>The printer identifier.</returns>
    public static PrinterId FromSpooler(string queueName) => new(PrinterIdKind.Spooler, queueName);

    /// <summary>
    /// Creates a network printer identifier from a host name or address.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <returns>The printer identifier.</returns>
    public static PrinterId FromNetwork(string host) => new(PrinterIdKind.Network, host);

    /// <summary>
    /// Creates a USB printer identifier from a device path or serial number.
    /// </summary>
    /// <param name="device">The device path or serial number.</param>
    /// <returns>The printer identifier.</returns>
    public static PrinterId FromUsb(string device) => new(PrinterIdKind.Usb, device);

    /// <inheritdoc />
    public bool Equals(PrinterId other) => Kind == other.Kind && String.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrinterId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((int)Kind, StringComparer.Ordinal.GetHashCode(Value));

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{Value}";

    /// <summary>
    /// Compares two identifiers for equality.
    /// </summary>
    public static bool operator ==(PrinterId left, PrinterId right) => left.Equals(right);

    /// <summary>
    /// Compares two identifiers for inequality.
    /// </summary>
    public static bool operator !=(PrinterId left, PrinterId right) => !left.Equals(right);
}
