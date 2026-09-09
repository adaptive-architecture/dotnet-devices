namespace AdaptArch.Devices.Printing.Spooler;

// Picks the one driver that can reach the local OS print spooler: WindowsSpoolerDriver
// on Windows, because there is no local IPP server to talk to there, and
// CupsSpoolerDriver everywhere else, because CUPS already speaks IPP on localhost.
internal static class SpoolerDriverFactory
{
    // WindowsSpoolerDriver allocates nothing: winspool.drv needs no HttpClient.
    // CupsSpoolerDriver builds and owns its own client, because it is the only
    // caller that needs one, so no caller of this method needs to supply one.
    public static ISpoolerDriver Create() =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver()
            : new CupsSpoolerDriver();
}
