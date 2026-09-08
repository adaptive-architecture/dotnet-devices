namespace AdaptArch.Devices.Printing;

/// <summary>
/// Endpoint of a printer connected over USB, identified by USB descriptors.
/// </summary>
public sealed class UsbPrinterEndpoint : PrinterEndpoint, IEquatable<UsbPrinterEndpoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UsbPrinterEndpoint"/> class.
    /// </summary>
    /// <param name="vendorId">The USB vendor identifier.</param>
    /// <param name="productId">The USB product identifier.</param>
    /// <param name="serialNumber">The USB serial number, when available.</param>
    public UsbPrinterEndpoint(int vendorId, int productId, string? serialNumber = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vendorId);
        ArgumentOutOfRangeException.ThrowIfNegative(productId);
        VendorId = vendorId;
        ProductId = productId;
        SerialNumber = serialNumber;
    }

    /// <inheritdoc />
    public override PrinterIdKind Kind => PrinterIdKind.Usb;

    /// <summary>
    /// Gets the USB vendor identifier.
    /// </summary>
    public int VendorId { get; }

    /// <summary>
    /// Gets the USB product identifier.
    /// </summary>
    public int ProductId { get; }

    /// <summary>
    /// Gets the USB serial number, when available.
    /// </summary>
    public string? SerialNumber { get; }

    /// <inheritdoc />
    public bool Equals(UsbPrinterEndpoint? other) =>
        other is not null &&
        VendorId == other.VendorId &&
        ProductId == other.ProductId &&
        string.Equals(SerialNumber, other.SerialNumber, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UsbPrinterEndpoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(VendorId, ProductId, SerialNumber);

    /// <inheritdoc />
    public override string ToString() => SerialNumber is null
        ? $"USB:{VendorId:X4}:{ProductId:X4}"
        : $"USB:{VendorId:X4}:{ProductId:X4}:{SerialNumber}";
}
