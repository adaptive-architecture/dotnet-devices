using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

// Windows has no local IPP server; everywhere else CUPS already speaks IPP on localhost.
internal static class SpoolerDriverFactory
{
    // Each driver owns whatever it needs, so no caller supplies an HttpClient. The IPP
    // policy reaches the CUPS driver only: it speaks IPP, and the Windows driver does not.
    // The log reaches both.
    public static ISpoolerDriver Create(
        PrintFormatPolicy? formats = null,
        IppTransportOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IPrintCapture? capture = null) =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver(formats, loggerFactory) { Capture = capture }
            : new CupsSpoolerDriver(formats, options, loggerFactory) { Capture = capture };
}
