using System.Linq;

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
    /// <exception cref="ArgumentException">
    /// Thrown when the name is blank, is longer than 127 characters, or contains a
    /// control character or one of <c>/</c>, <c>?</c> and <c>#</c>.
    /// </exception>
    public SpoolerPrinterEndpoint(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsValidName(name))
        {
            throw new ArgumentException("A print queue name has at most 127 characters and contains no control character and none of '/', '?' or '#'.", nameof(name));
        }

        Name = name;
    }

    // The CUPS limit is 127 characters. The rejected characters would end the URI path
    // segment the CUPS driver builds from the name. A backslash stays allowed: a Windows
    // printer connection is named \\server\queue.
    private static bool IsValidName(string name) =>
        name.Length <= 127 && !name.Any(static c => Char.IsControl(c) || c is '/' or '?' or '#');

    /// <inheritdoc />
    public override PrinterIdKind Kind => PrinterIdKind.Spooler;

    /// <summary>
    /// Gets the operating system print queue name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>Queue names are compared without regard to case, as Windows and CUPS compare them.</remarks>
    public bool Equals(SpoolerPrinterEndpoint? other) =>
        other is not null && String.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpoolerPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    /// <inheritdoc />
    public override string ToString() => Name;
}
