using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Reads what an IPP printer says about which device it is. Both attributes are optional:
// a printer that reports neither is not an error, it is simply a printer this library
// cannot recognise again at another address.
internal static class IppIdentityMapper
{
    public static readonly string[] RequestedAttributes =
    [
        "printer-uuid",
        "printer-device-id",
        "printer-make-and-model",
        "printer-name",
        "printer-location",
    ];

    public static PrinterIdentity Map(PrinterDescriptionAttributes? attributes)
    {
        if (attributes is null)
        {
            return new PrinterIdentity();
        }

        // The wire form is "urn:uuid:<36 characters>", and the mDNS TXT form is the bare
        // UUID, so both are normalised to the same text or the two never match.
        var uuid = PrinterId.TryParseDeviceUuid(attributes.PrinterUUID, out var parsed)
            ? parsed.ToString("D")
            : null;

        _ = Ieee1284DeviceId.TryParse(attributes.PrinterDeviceId, out var deviceId);

        return new PrinterIdentity
        {
            Uuid = uuid,
            SerialNumber = deviceId.SerialNumber,
            Manufacturer = deviceId.Manufacturer,
            Model = deviceId.Model,
            CommandSets = deviceId.CommandSets,
            MakeAndModel = attributes.PrinterMakeAndModel,
            Name = attributes.PrinterMakeAndModel ?? attributes.PrinterName,
            Location = attributes.PrinterLocation,
        };
    }
}
