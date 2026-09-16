using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns a raw IPP answer into text the public API can carry. SharpIppNext types stay out of
// the public surface, which docs/packages.md requires.
internal static class IppRawSnapshot
{
    public static IReadOnlyList<IppAttributeSnapshot> Project(IIppResponseMessage? response, bool capture)
    {
        if (!capture || response is null)
        {
            return [];
        }

        List<IppAttributeSnapshot> attributes = [];
        Add(attributes, "operation", response.OperationAttributes);
        Add(attributes, "printer", response.PrinterAttributes);
        Add(attributes, "job", response.JobAttributes);
        Add(attributes, "unsupported", response.UnsupportedAttributes);
        return attributes;
    }

    private static void Add(List<IppAttributeSnapshot> target, string group, List<List<IppAttribute>> groups)
    {
        foreach (var attributes in groups)
        {
            foreach (var attribute in attributes)
            {
                target.Add(new IppAttributeSnapshot(group, attribute.Name, IppRawAttributes.GetText(attribute.Value)));
            }
        }
    }
}
