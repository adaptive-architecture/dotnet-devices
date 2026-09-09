namespace AdaptArch.Devices.Printing;

/// <summary>
/// The object identifiers that the SNMP status client reads. The scalar identifiers end
/// with a row index. The column identifiers have no index, because a table walk appends
/// the index of each row.
/// </summary>
internal static class PrinterMibOids
{
    /// <summary>
    /// <c>sysDescr</c> from MIB-II. A description of the device.
    /// </summary>
    public const string SystemDescription = "1.3.6.1.2.1.1.1.0";

    /// <summary>
    /// <c>sysName</c> from MIB-II. The administrative name of the device.
    /// </summary>
    public const string SystemName = "1.3.6.1.2.1.1.5.0";

    /// <summary>
    /// <c>sysLocation</c> from MIB-II. The place of the device.
    /// </summary>
    public const string SystemLocation = "1.3.6.1.2.1.1.6.0";

    /// <summary>
    /// <c>prtGeneralPrinterName</c>, column 16 of <c>prtGeneralTable</c> in RFC 3805.
    /// </summary>
    public const string PrinterName = "1.3.6.1.2.1.43.5.1.1.16.1";

    /// <summary>
    /// <c>prtGeneralSerialNumber</c>, column 17 of <c>prtGeneralTable</c> in RFC 3805.
    /// </summary>
    public const string SerialNumber = "1.3.6.1.2.1.43.5.1.1.17.1";

    /// <summary>
    /// <c>prtMarkerLifeCount</c>. The number of pages that the printer has marked.
    /// </summary>
    public const string MarkerLifeCount = "1.3.6.1.2.1.43.10.2.1.4.1.1";

    /// <summary>
    /// <c>hrPrinterStatus</c> from the Host Resources MIB.
    /// </summary>
    public const string PrinterStatus = "1.3.6.1.2.1.25.3.5.1.1.1";

    /// <summary>
    /// <c>hrPrinterDetectedErrorState</c> from the Host Resources MIB. A bit string.
    /// </summary>
    public const string DetectedErrorState = "1.3.6.1.2.1.25.3.5.1.2.1";

    /// <summary>
    /// <c>prtMarkerSuppliesDescription</c>, column 6 of <c>prtMarkerSuppliesTable</c>.
    /// </summary>
    public const string SuppliesDescription = "1.3.6.1.2.1.43.11.1.1.6";

    /// <summary>
    /// <c>prtMarkerSuppliesMaxCapacity</c>, column 8 of <c>prtMarkerSuppliesTable</c>.
    /// </summary>
    public const string SuppliesMaxCapacity = "1.3.6.1.2.1.43.11.1.1.8";

    /// <summary>
    /// <c>prtMarkerSuppliesLevel</c>, column 9 of <c>prtMarkerSuppliesTable</c>.
    /// </summary>
    public const string SuppliesLevel = "1.3.6.1.2.1.43.11.1.1.9";

    /// <summary>
    /// <c>prtMarkerSuppliesColorantIndex</c>, column 10 of <c>prtMarkerSuppliesTable</c>.
    /// It names the row of <see cref="ColorantValue"/> that gives the color of a supply.
    /// </summary>
    public const string SuppliesColorantIndex = "1.3.6.1.2.1.43.11.1.1.10";

    /// <summary>
    /// <c>prtMarkerColorantValue</c>, column 4 of <c>prtMarkerColorantTable</c>.
    /// </summary>
    public const string ColorantValue = "1.3.6.1.2.1.43.12.1.1.4";

    /// <summary>
    /// Gets the scalar identifiers that one GET request reads.
    /// </summary>
    public static IReadOnlyList<string> Scalars { get; } =
    [
        SystemDescription,
        SystemName,
        SystemLocation,
        PrinterName,
        SerialNumber,
        MarkerLifeCount,
        PrinterStatus,
        DetectedErrorState,
    ];

    /// <summary>
    /// Gets the table columns that the supply walk reads.
    /// </summary>
    public static IReadOnlyList<string> SupplyColumns { get; } =
    [
        SuppliesDescription,
        SuppliesMaxCapacity,
        SuppliesLevel,
        SuppliesColorantIndex,
        ColorantValue,
    ];
}
