namespace AdaptArch.Devices.Printing;

/// <summary>
/// One attribute of a raw IPP response, as text.
/// </summary>
/// <remarks>
/// The library captures these only when <see cref="IppTransportOptions.CaptureRawResponses"/>
/// is <c>true</c>. They carry what the printer sent, including the attributes the library
/// does not map, so a user can see the whole answer when a job stops for an unknown reason.
/// </remarks>
/// <param name="Group">The attribute group: <c>operation</c>, <c>printer</c>, <c>job</c> or <c>unsupported</c>.</param>
/// <param name="Name">The attribute name, as the printer sent it.</param>
/// <param name="Value">The attribute value, as text.</param>
public sealed record IppAttributeSnapshot(string Group, string Name, string Value);
