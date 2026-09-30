namespace AdaptArch.Devices.Printing;

/// <summary>
/// What <see cref="IPrinter.PrintAsync"/> does with an option the printer does not support.
/// </summary>
/// <remarks>
/// A named or required converter is not such an option: one that cannot run fails the job
/// with <see cref="NotSupportedException"/> whichever value is chosen, because it says which
/// engine renders the job. See <see cref="PrintOptions.ConverterName"/>.
/// </remarks>
public enum UnsupportedOptionBehavior
{
    /// <summary>
    /// Send the option and let the printer decide. This costs no extra request.
    /// </summary>
    Send,

    /// <summary>
    /// Read the configuration first, then throw <see cref="NotSupportedException"/>. A spooler
    /// channel, Windows or CUPS, also throws before it submits when it cannot apply an option.
    /// </summary>
    Throw,

    /// <summary>
    /// Read the configuration first, remove the option, and name it in
    /// <see cref="PrintJobInfo.DroppedOptions"/>.
    /// </summary>
    Drop,
}
