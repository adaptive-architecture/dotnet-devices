namespace AdaptArch.Devices.Printing;

/// <summary>
/// The print quality a job asks for. The numbers are the RFC 8011 values.
/// </summary>
public enum PrintQuality
{
    /// <summary>The lowest quality the printer has.</summary>
    Draft = 3,

    /// <summary>The middle quality the printer has.</summary>
    Normal = 4,

    /// <summary>The highest quality the printer has.</summary>
    High = 5,
}
