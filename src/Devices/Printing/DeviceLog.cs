using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdaptArch.Devices.Printing;

// The one line every log class of the library shares.
//
// A string category, not CreateLogger<T>(), so one filter rule turns a whole area on and no
// internal type name leaks into a configuration file. A category is a filter name and not a
// namespace, so it stays stable when a type moves. The categories nest:
//
//   AdaptArch.Devices.Printing            the manager, the routing, the job path
//   AdaptArch.Devices.Printing.Ipp        the IPP wire
//   AdaptArch.Devices.Printing.Discovery  mDNS, SNMP, the probe, the correlator
//   AdaptArch.Devices.Printing.Spooler    the Windows spooler
//
// so a filter on "AdaptArch.Devices" turns on everything.
internal static class DeviceLog
{
    // NullLogger, never null: each generated method then stops at its own IsEnabled guard,
    // so an application that registers nothing pays one virtual call for each log point.
    public static ILogger Create(ILoggerFactory? factory, string category) =>
        factory?.CreateLogger(category) ?? NullLogger.Instance;
}
