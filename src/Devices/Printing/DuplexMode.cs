namespace AdaptArch.Devices.Printing;

/// <summary>
/// Duplex (double-sided) printing mode.
/// </summary>
public enum DuplexMode
{
    /// <summary>
    /// Single-sided printing.
    /// </summary>
    Simplex,

    /// <summary>
    /// Double-sided printing with long-edge binding.
    /// </summary>
    LongEdge,

    /// <summary>
    /// Double-sided printing with short-edge binding.
    /// </summary>
    ShortEdge,
}
