namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for SNMP status queries. All operations are read-only; the client writes
/// nothing to the printer.
/// </summary>
public sealed class SnmpPrinterStatusOptions
{
    /// <summary>
    /// Gets or sets the community string. Defaults to <c>public</c>, which is what
    /// printers on a local network normally accept for read access.
    /// </summary>
    public string Community { get; set; } = "public";

    /// <summary>
    /// Gets or sets the time to wait for one answer. Defaults to two seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how many times to send a request again when no answer arrives.
    /// Defaults to two, which gives three attempts, because UDP can lose a datagram.
    /// </summary>
    public int Retries { get; set; } = 2;
}
