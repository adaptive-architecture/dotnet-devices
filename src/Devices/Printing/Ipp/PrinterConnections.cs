namespace AdaptArch.Devices.Printing.Ipp;

// Names the transport of an endpoint that answered. A URI that no scheme matches gives
// null, so a caller never reads a scheme the library did not recognise.
internal static class PrinterConnections
{
    public static PrinterConnection? From(Uri? uri) =>
        uri is not null && PrinterSchemes.TryParse(uri.Scheme, out var scheme)
            ? new PrinterConnection(scheme, uri)
            : null;
}
