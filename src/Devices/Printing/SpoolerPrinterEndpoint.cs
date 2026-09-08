namespace AdaptArch.Devices.Printing;

/// <summary>
/// Endpoint of a printer installed in the operating system print spooler,
/// addressed by queue name.
/// </summary>
public sealed class SpoolerPrinterEndpoint : PrinterEndpoint, IEquatable<SpoolerPrinterEndpoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrinterEndpoint"/> class.
    /// </summary>
    /// <param name="name">The operating system print queue name.</param>
    public SpoolerPrinterEndpoint(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <inheritdoc />
    public override PrinterIdKind Kind => PrinterIdKind.Spooler;

    /// <summary>
    /// Gets the operating system print queue name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    public bool Equals(SpoolerPrinterEndpoint? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpoolerPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <inheritdoc />
    public override string ToString() => Name;
}
