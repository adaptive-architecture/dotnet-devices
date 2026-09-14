namespace AdaptArch.Devices.Printing.Spooler;

// Windows has no local IPP server; everywhere else CUPS already speaks IPP on localhost.
internal static class SpoolerDriverFactory
{
    // Each driver owns whatever it needs, so no caller supplies an HttpClient.
    public static ISpoolerDriver Create(PrintFormatPolicy? formats = null) =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver(formats)
            : new CupsSpoolerDriver(formats);
}
