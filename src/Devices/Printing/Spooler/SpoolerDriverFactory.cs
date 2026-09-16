using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

// Windows has no local IPP server; everywhere else CUPS already speaks IPP on localhost.
internal static class SpoolerDriverFactory
{
    // Each driver owns whatever it needs, so no caller supplies an HttpClient. The IPP
    // policy reaches the CUPS driver only: it speaks IPP, and the Windows driver does not.
    public static ISpoolerDriver Create(
        PrintFormatPolicy? formats = null,
        IppTransportOptions? options = null,
        ILoggerFactory? loggerFactory = null) =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver(formats, loggerFactory)
            : new CupsSpoolerDriver(formats, options);
}
