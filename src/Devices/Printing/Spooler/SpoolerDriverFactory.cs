namespace AdaptArch.Devices.Printing.Spooler;

// Windows has no local IPP server; everywhere else CUPS already speaks IPP on localhost.
internal static class SpoolerDriverFactory
{
    // Each driver owns whatever it needs, so no caller supplies an HttpClient.
    public static ISpoolerDriver Create() =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver()
            : new CupsSpoolerDriver();
}
