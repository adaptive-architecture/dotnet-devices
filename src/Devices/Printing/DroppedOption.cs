namespace AdaptArch.Devices.Printing;

/// <summary>
/// An option a job asked for that did not reach the device, where that happened and why.
/// </summary>
/// <param name="Option">The name of the <see cref="PrintOptions"/> member, for example <c>Placement</c>.</param>
/// <param name="Stage">The step of the print path that dropped it.</param>
/// <param name="Reason">Why it was dropped, written for a person to read.</param>
public sealed record DroppedOption(string Option, PrintOptionStage Stage, string Reason);
