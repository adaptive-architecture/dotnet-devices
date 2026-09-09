namespace AdaptArch.Devices.Printing;

/// <summary>
/// What <see cref="IPrinter.PrintAsync"/> does with an option the printer does not support.
/// </summary>
public enum UnsupportedOptionBehavior
{
    /// <summary>
    /// Send the option and let the printer decide. This costs no extra request.
    /// </summary>
    Send,

    /// <summary>
    /// Read the configuration first, then throw <see cref="NotSupportedException"/>.
    /// </summary>
    Throw,

    /// <summary>
    /// Read the configuration first, remove the option, and name it in
    /// <see cref="PrintJobInfo.DroppedOptions"/>.
    /// </summary>
    Drop,
}
