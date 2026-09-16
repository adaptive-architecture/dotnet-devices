namespace AdaptArch.Devices.Printing.Ipp;

// The IPP status codes the library reacts to (RFC 8011 section 13.1). Only these few are
// named: PrinterOperationException reports the code as a number, so a caller matches any
// other one without a library constant.
internal static class IppStatusCodes
{
    public const int ClientErrorNotFound = 0x0406;

    public const int ClientErrorDocumentFormatNotSupported = 0x040A;
}
