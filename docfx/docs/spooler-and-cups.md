# Spooler and CUPS

A print queue of the operating system is a channel like any other, with the `spooler` scheme.
The same code reaches a CUPS server over the network, with the `cups` scheme.

## Two drivers, one API

`SpoolerPrinterDiscovery`, `SpoolerPrintJobQueue` and `SpoolerPrinter` reach the operating
system spooler through one of two drivers, chosen at run time:

- **`CupsSpoolerDriver`** — Linux and macOS. CUPS runs its own IPP server on `localhost:631`,
  so this driver sends the same IPP requests, aimed at the local daemon. It needs no native
  interop, which is also what makes it the driver behind the `cups` scheme.
- **`WindowsSpoolerDriver`** — Windows, through `winspool.drv`. Windows has no local IPP server,
  so this driver is the one part of the library that calls native code.

Because the CUPS driver speaks IPP, a `spooler://` printer on Linux and macOS reports everything
an IPP channel reports: the connection, the state messages, the raw attributes. A Windows
`spooler://` printer reports the state and its reasons and nothing else.

## Where each path runs

| What prints | Where it runs |
| :--- | :--- |
| IPP, raw TCP, SNMP and discovery | Every platform .NET 10 supports, Windows, Linux and macOS alike. |
| The spooler: printer languages, PNG and JPEG | Every Windows .NET 10 supports, Server Core included, because `winspool.drv`, `gdi32` and `gdiplus` ship in-box on all of them. Not Nano Server, which has neither GDI nor a spooler. |
| The spooler: PDF, with `AdaptArch.Devices.Windows` | Windows 10, Windows 11, and Windows Server with the Desktop Experience. The engine is WinRT, so Windows Server 2012 R2 has none and Server Core is untested. A machine without it gets `PlatformNotSupportedException` and not a complaint about the file. |
| The spooler: PDF, with `AdaptArch.Devices.Pdfium` | Every Windows the row above covers, Server Core and Server 2012 R2 included: PDFium is a native library the package carries and owes nothing to the installation. Not Nano Server, which has no spooler to print through. |

> [!NOTE]
> Windows Protected Print Mode, which an administrator can turn on from Windows 11 24H2 and
> Windows Server 2025, blocks third-party drivers and leaves only the Microsoft IPP class
> driver. A queue behind it takes no printer language through a vendor driver; the IPP transport
> of this library is untouched.

## The Windows spooler

The Windows driver builds a `DEVMODE` for the job with `DocumentProperties` and carries it onto
the job:

- **Printer languages** use the `RAW` data type and pass the bytes through unchanged.
- **PNG and JPEG** are drawn onto a GDI printer device context with GDI+, so the driver
  rasterises the page. This uses only the system `gdi32.dll` and `gdiplus.dll` and no extra
  NuGet package.
- **PDF pages** render to PNG first, then print as one GDI document through the same path. That
  renderer lives outside the core package; the application lights one up with
  `EnablePdfPrinting()`. Without a converter for PDF a job fails with `NotSupportedException`
  before anything spools, and so does one whose converter writes no PNG: GDI draws that and
  nothing else.

Any other document format prints the same way once the application
[registers a converter](document-formats.md#add-a-format-the-library-does-not-know) for it.

### What a device mode can and cannot hold

Two rules decide what a field can hold:

- `MediaSize` and `MediaSource` are names, and a `DEVMODE` field holds a number, so the name is
  looked up in the media and tray lists the queue reports. A name the queue did not report has
  no number, and is dropped. The lists cost two spooler calls each, so they are read only when
  the job names a size or a tray.
- `dmPrintQuality` holds either a resolution in dots per inch or a `DMRES_*` quality name. A job
  that sets both keeps the resolution, because it is the exact number the caller gave.

`Copies` is applied by printing the document once for each copy, because a queue with the `RAW`
data type never reads `dmCopies`. Each copy is a separate spooler job and the returned
`PrintJobInfo` names the first of them, so a failure on a later copy leaves the earlier copies in
the queue — which is what a paper jam also does. **Image jobs are the exception**: the GDI path
honours `dmCopies`, so one job prints every copy.

`MediaType`, `OutputBin`, `PageRanges` and `NumberUp` have no `DEVMODE` field, so the driver
always reports them in `PrintJobInfo.DroppedOptions`, whatever `OnUnsupported` says. Two more are
carried only in part for printer languages: `dmScale` is a percentage and not a fit mode, so
`Scaling` reaches it as `PrintScaling.None` and the other four values are dropped; and
`dmOrientation` holds portrait and landscape and nothing else, so `ReverseLandscape` and
`ReversePortrait` are dropped as well. **Image jobs lay out with GDI instead** and honour every
orientation and every scaling mode, so neither is dropped there. IPP and CUPS carry all of them.

[Page placement](page-placement.md) covers how a GDI image job is sized and positioned.

### Queue names

A `SpoolerPrinterEndpoint` name has at most 127 characters and contains no control character and
none of `/`, `?` and `#`, which would end the path segment the CUPS driver builds from it. Spaces
and a Windows connection name such as `\\server\queue` are accepted, and two names are equal
without regard to case, as Windows and CUPS compare them.

> [!NOTE]
> Native Windows calls cannot run in this repository's test suite or CI, which both run on Linux.
> Everything around the native calls is tested through seams; the calls themselves are checked by
> hand on real Windows hardware.

## A CUPS server, from any operating system

`SpoolerPrinter` reaches the daemon on `localhost`, so it finds queues only where CUPS runs.
The daemon is an IPP server and nothing about talking to it is platform-specific, so the same
calls reach a CUPS server over the network — from Windows, from a container, and from a machine
with no spooler of its own. That is the `cups` scheme.

A CUPS server announces nothing on the local link, so it is found only when it is named:

```csharp
services.AddPrinters(
    configure: transport =>
    {
        // CUPS asks for Basic, which is clear text over plain IPP.
        transport.AllowPlainIpp = false;
        transport.Credentials = new NetworkCredential("print", "…");
    },
    configureManager: options => options.CupsServers.Add(new CupsServer("printsrv")));
```

Discovery then reports every queue of that server as a `cups://printsrv/{queue}` channel, next to
the queues of this machine. Everything downstream is unchanged: `IPrinterManager` routes to it,
`CompositePrintJobQueue` reads its jobs, and the device behind each queue still reaches
`DiscoveredPrinter.Aliases`, so a queue found here and the same printer found over multicast DNS
group into one `PrinterDevice`.

Four things are worth knowing:

- **`IncludeSpooler` gates both.** The local spooler and the named servers are one discovery
  source, because both of them find queues. An application that turns it off asks about no queue
  at all, wherever the queue lives.
- **A server that does not answer does not fail the discovery.** The queues of every other source
  are still reported.
- **The transport policy chooses IPPS or plain IPP**, by the same `AllowPlainIpp` switch every
  other IPP channel reads. The identifier names the queue and not the security of the channel,
  and unlike a printer, a CUPS server serves both on one port, so nothing is probed.
- **Credentials live on `IppTransportOptions`**, with the rest of the transport policy. Set
  `Credentials` to a `CredentialCache` to give two servers two passwords.

Windows has no local IPP server of its own, so this is what a CUPS queue looks like from there.
It is not a port of the daemon: the queue stays on the server, and the client speaks to it.

## USB printers

**This library does not talk to a USB printer directly, and does not intend to.** There is no USB
transport, no USB endpoint and no `usb` scheme; `PrinterId.TryParse` returns `false` for a
`usb://…` text.

Print to a USB printer through the operating system queue, with a `spooler://` identifier. The
queue already owns the device, `SpoolerPrinter` already prints to it on all three platforms, and
`SpoolerPrinterDiscovery` finds it.

**Grouping still works.** A CUPS queue reports `device-uri` as
`usb://Zebra/ZTC%20ZD421?serial=X4TY012345`, and `DeviceUriParser` reads the serial number out of
it. That serial becomes the same device key an IPP or SNMP read produces, so a queue and the
network channels of one printer still merge into one `PrinterDevice`.

A device that is reachable only over USB and is **not** installed in the print queue is out of
reach of this library. Install it in the queue first. [Roadmap](roadmap.md#usb-printers) says why
this will not change.
