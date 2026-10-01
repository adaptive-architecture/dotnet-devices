# Capabilities

What the library does, and what is planned, is in the
[Roadmap](https://adaptive-architecture.github.io/dotnet-devices/docs/roadmap.html). This page
records the evidence behind it.

## Test evidence

**The IPP and CUPS channels are tested against real servers.**
`test/Devices.IntegrationTests` runs a CUPS daemon and `ippeveprinter`, the CUPS project's own
IPP Everywhere server, in containers, and prints to them: capabilities, job submission, the
job list, the job state, cancellation, a watch that ends on a cancelled job, and the choice
between sending a PDF and converting it, taken from what the printer itself advertises, and a
PDF rendered to URF and to PWG Raster for a CUPS queue.
[Development](development.md#integration-tests) says how to run them.

**The Windows spooler has printed on real Windows hardware, most recently on 2026-09-19**:
the job sweep, the full print cycle, the error paths, the configuration against two real
drivers, a native AOT publish exercising every driver method, PDF through the spooler, and
PWG Raster over IPP — the path that until then had run nowhere. Everything around the native
calls is tested on Linux: `WindowsSpoolerDriver` and `WindowsGdiImagePrinter` reach the
spooler and GDI through seams, and a fake answers them with the structures the real ones
write, so the buffer protocol, the page loop and the error paths run in the ordinary suite.
**What has not met hardware is the placement work**, which postdates that session: it is
arithmetic with unit tests, and where a page actually lands is a question only a ruler
answers. [Windows manual tests](windows-manual-tests.md) lists each run and what is left.

**Text, email and images laid out as PDF have met no printer yet.** Unit tests check the PDF
the library writes for each format and what each channel sends, and the files were rendered
by PDFium, poppler and Ghostscript, which agreed. Where a driver puts a text page is
[manual test 13](windows-manual-tests.md#13-text-email-and-images-laid-out-as-pdf).
