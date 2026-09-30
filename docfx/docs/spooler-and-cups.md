# Spooler and CUPS

A print queue of the operating system is a channel like any other, with the `spooler` scheme.
The same code reaches a CUPS server over the network, with the `cups` scheme.

## Two drivers, one API

`SpoolerPrinterDiscovery`, `SpoolerPrintJobQueue` and `SpoolerPrinter` reach the operating
system spooler through one of two drivers, chosen at run time:

- **`CupsSpoolerDriver`** — Linux and macOS. CUPS runs its own IPP server on `localhost:631`,
  so this driver sends the same IPP requests, aimed at the local daemon. It connects through the
  daemon's domain socket (`/private/var/run/cupsd` on macOS, `/run/cups/cups.sock` on Linux)
  where one exists, because macOS starts `cupsd` on demand from that socket and refuses
  `localhost:631` while the daemon is idle; it falls back to TCP otherwise. It needs no native
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

`Copies` is applied by writing the payload once for each copy, each as a page of one spooler job,
because a queue with the `RAW` data type never reads `dmCopies`. The printer receives the same
bytes as it would from one job per copy, but `PrintJobInfo.JobId` names every copy: cancelling it
cancels them all, and a failure part-way deletes the job, so no copy is left in the queue. Copies
the spooler has already sent to the printer can still print. **Image and PDF jobs are the
exception**: the GDI path honours `dmCopies`.

`MediaType`, `OutputBin`, `PageRanges` and `NumberUp` have no `DEVMODE` field, so the driver
reports them in `PrintJobInfo.DroppedOptions`, or refuses the job before it spools when `OnUnsupported` is `Throw`. Two more are
carried only in part for printer languages: `dmScale` is a percentage and not a fit mode, so
`Scaling` reaches it as `PrintScaling.None` and the other four values are dropped; and
`dmOrientation` holds portrait and landscape and nothing else, so `ReverseLandscape` and
`ReversePortrait` are dropped as well. **Image jobs lay out with GDI instead** and honour every
orientation and every scaling mode, so neither is dropped there. IPP and CUPS carry all of them.

**A short device mode is refused, not worked around.** A job fails with
`InvalidOperationException` when the driver reports a device mode smaller than `DEVMODEW`,
because writing the fields back would overwrite the driver-private data behind it.

The defaults on `PrinterConfiguration` come from the same device mode.
[Page placement](page-placement.md) covers how a GDI image job is sized and positioned.

### Queue names

A `SpoolerPrinterEndpoint` name has at most 127 characters and contains no control character and
none of `/`, `?` and `#`, which would end the path segment the CUPS driver builds from it. Spaces
and a Windows connection name such as `\\server\queue` are accepted, and two names are equal
without regard to case, as Windows and CUPS compare them.

## The default queue

`PrinterInfo.IsDefault`, and through it `PrinterDevice.IsDefault`, marks the queue a print
command would pick when it is given none. Windows reports the queue the spooler marks as
default. On Linux and macOS the driver reports the queue `lp` would pick. It follows the order
libcups uses and stops at the first one that names a queue:

1. `LPDEST`, then `PRINTER`. A `PRINTER` of `lp` is ignored, as libcups ignores it.
2. The `Default` line of `~/.cups/lpoptions`, which `lpoptions -d` writes. It is not read for a
   process that runs as root.
3. The `Default` line of `$CUPS_SERVERROOT/lpoptions`, or of `/etc/cups/lpoptions` when the
   variable is unset.
4. The server default, which `lpadmin -d` sets. CUPS marks it in the `printer-type` of each
   queue that `CUPS-Get-Printers` returns, so finding it costs no extra request.

An instance (`office/draft`) counts as its queue. When steps 1–3 name a queue that does not
exist, no queue is the default. The server default does not stand in, because `lp` fails in that
case too. The macOS "use last printer" preference is not read, because reading it needs
CoreFoundation. A queue of a named [CUPS server](#a-cups-server-from-any-operating-system)
reports the server default only, because the settings of this machine do not apply to another
host.

## Queue defaults on CUPS

A spooler job carries only the options it sets, so every option it leaves unset takes the
**queue's** default — and a queue's defaults are not the printer's. A driverless queue on macOS
shows how far apart they can be:

- **Two-sided by default.** The queue's PPD reads `*DefaultDuplex: DuplexNoTumble` while the
  printer reports `sides-default = one-sided`. A job that names no `Duplex` prints on both sides
  through the spooler, and on one side over IPP.
- **Reverse output order.** `*DefaultOutputOrder: Reverse`, so the first page of a job comes out
  last.
- **A PDF is fitted only when the job says so.** macOS CUPS renders a PDF with Quartz and not
  with cups-filters. With no media in the job it takes the PDF's own page size as the media, so a
  4 by 6 inch label sent to a printer loaded with A4 arrives as a 4 by 6 inch page and the printer
  reports a size mismatch. With `media=A4` it draws the page from the PDF's origin, against the
  bottom left of the sheet, and ignores `print-scaling`: `none`, `fit` and `auto` gave the same
  output. Only the CUPS job attribute `fit-to-page` makes it centre the page, and it then fits
  exactly as `auto` does: a page that fits keeps its size, a larger one is shrunk into the
  printable area, and a landscape page is turned onto portrait media. The library therefore
  sends every PDF job it does not render with its media (the job's own, or the queue's
  `media-default`), `print-scaling` (the job's, or `auto`) and `fit-to-page`. Linux reads
  `print-scaling` first and centres the page as it always did, and the Windows spooler places it
  the same way with GDI. A job with `MediaSizeSource.Document` is sent with neither media nor
  fit, and Quartz prints the page at its own size.

To see and change them:

- `lpoptions -p <queue> -l` lists a queue's options with the current value starred.
- Set the option on the job to override one, such as `Duplex = DuplexMode.Simplex`, or change the
  queue itself with `lpadmin -p <queue> -o Duplex=None`.
- A job that must land in the same place on every platform asks for a
  [placement](page-placement.md) and names or requires a converter, so the library renders the
  page rather than leaving it to the queue. Without a converter, CUPS places the page and the
  placement is reported dropped.

> [!NOTE]
> This was measured with `lpoptions`, `ipptool` and `cupsfilter` on macOS 27 against an EPSON
> L6270, with nothing printed. It is the queue and not the library that decides it, so recheck
> with `cupsfilter` on another macOS version.

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
