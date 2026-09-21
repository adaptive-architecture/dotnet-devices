# Windows Manual Tests

The Windows spooler driver speaks to `winspool.drv` through native interop. Most of what
sits around those calls is tested on Linux: every one goes through `IWindowsSpoolerInterop`
or `IWindowsGdiInterop`, and a fake spooler answers with real `PRINTER_INFO_4`,
`PRINTER_INFO_2`, `JOB_INFO_2` and `DEVMODEW` bytes, so the buffer protocol, the structure
layouts, the page loop and the error paths all run there.

The calls themselves run on the `windows` CI job, against a print queue it creates and
pauses. That is what settles whether a structure is declared as the real header declares it,
which a fake cannot: it writes the structure with the same declaration it reads it with.

What is left for a person is what only real paper and a real driver can settle — that the
bytes we send make the right marks, that a second driver answers for its own media, and that
the marshalling holds under a native AOT publish.

This page lists what a person must test on a real Windows machine, and what the automated
tests already prove.

## What has been run

Each session is written up in [windows-manual-tests-resuls/](windows-manual-tests-resuls/),
one file a run, and that folder is the record. The summary below is only the state it leaves
this page in.

**2026-09-19, Windows 11, AOT publish of the sample, against Microsoft Print to PDF and a
real Epson driver.** Passed: the printer status bits that a WSD monitor raises at all (T2.1,
T2.4), the full print cycle with its four dropped options (T3), a native AOT publish
exercising every `ISpoolerDriver` method (T4), the error paths including a stopped spooler
(T5), the configuration against both drivers and its defaults following the driver dialog
(T6), the access level (T7), two identity cases, PDF through the spooler including the
corrupt file and the page range (T9), and PWG Raster over IPP (T10) — the path that had
never run anywhere. The session also found and fixed a discovery bug: one queue with a `/`
in its name aborted the whole enumeration.

Left open by that session: the T3 variants the sample button skips, identity cases 2 and
4–7, and a T10 re-run for the duplex back-side fix it produced.

**The placement work of 2026-09-21 postdates that session**, so test 11 below has never run.
Everything in it is exercised through the seams on Linux, and none of it has met a ruler.

## What the automated tests already prove

These tests run in the usual suite, on Linux, and they need no Windows machine. The driver
keeps the native calls behind a seam, so everything around them is testable.

| Area | Test class | What it proves |
| --- | --- | --- |
| Printer status bits | `WindowsSpoolerStatusMapperTests` | Each `PRINTER_STATUS_*` bit maps to the correct `PrinterStatusState`, the precedence is correct when two bits are set together, and each bit is named correctly in `PrinterStatus.Detail`. The `PRINTER_ATTRIBUTE_WORK_OFFLINE` attribute maps to `Offline`. |
| Device mode options | `WindowsSpoolerDeviceModeMapperTests` | Each option maps to the correct `DEVMODE` field and `DM_*` bit, a media or tray name is looked up in the numbers the queue reported, a resolution wins over a quality on `dmPrintQuality`, and every option that reached no field is named in `DeviceModeRequest.Dropped`, so `PrintJobInfo.DroppedOptions` is correct. |
| Option support per channel | `PrinterSchemesTests` | `SupportedOptions` names what a Windows device mode carries, and everything the library models on CUPS and IPP. |
| Job status bits | `WindowsSpoolerStatusMapperTests` | Each `JOB_STATUS_*` bit maps to the correct `PrintJobState`, with the same precedence check. |
| Paper names | `WindowsSpoolerCapabilityParserTests` | `DC_PAPERNAMES` gives fixed 64-character blocks. The parser reads a short name with null padding, a name that fills all 64 characters with no terminator, an empty block, and several blocks in sequence. |
| Resolutions | `WindowsSpoolerCapabilityParserTests` | `DC_ENUMRESOLUTIONS` gives pairs of integers. The parser reads a list of pairs, one pair, and an empty buffer. |
| Platform guard | `WindowsSpoolerDriverTests` | All seven `ISpoolerDriver` methods throw `PlatformNotSupportedException` on a machine that is not Windows, before any native call. |
| Spooler buffer protocol | `WindowsSpoolerDriverSeamTests` | `EnumPrinters`, `GetPrinter` and `EnumJobs` are asked for the size and then for the data; an array of `JOB_INFO_2` is read back without misalignment, which is the check the structure layout only gets here; a queue with no driver reports no media rather than a guess. |
| Spooler error paths | `WindowsSpoolerDriverSeamTests` | A short `WritePrinter` keeps writing until the document is whole and a failed one deletes the job with `JOB_CONTROL_DELETE`, so no truncated label commits; a write that makes no progress fails instead of looping; `ERROR_INVALID_PARAMETER` from `SetJob` is a `false` and any other code is an exception; a failed `OpenPrinter` closes nothing, because it leaves the handle undefined; a device mode shorter than `DEVMODEW` is refused rather than written over. |
| Spooler job routing | `WindowsSpoolerDriverSeamTests` | A printer language goes out raw, an image and a document go through GDI, a copy count loops on the raw path and rides the device mode on the GDI path, and a page range reaches the converter on the document path only. |
| GDI page loop | `WindowsGdiImagePrinterTests` | Every page of a job is in one document; each page is written to its own temporary file, which is read back intact and deleted afterwards; a page that fails aborts the document rather than ending it and leaves no graphics, image or device context open; the resolution arithmetic reaches GDI+ as the rectangle it drew, and a page from a converter uses the resolution it was rendered at rather than the 96 the encoder left behind. |
| Windows PDF limits | `WindowsPdfLimitsTests` | The resolution is clamped to what the in-box engine renders well, and a page over the pixel cap keeps its shape because the cap belongs to the longer side. |
| Windows package surface | `WindowsPrintingTests` | `PdfConverter` reads PDF only and writes both PNG and PWG Raster, and adding it to `PrinterManagerOptions.Converters` enables PDF for one manager without touching the process. |
| Driver selection | `SpoolerDriverFactoryTests` | The factory gives a CUPS driver on Linux and macOS. |
| PWG Raster encoding | `PwgRasterWriterTests` | The synchronization word, a page header of exactly 1796 octets, the field offsets of PWG 5102.4 Table 1, and the PackBits encoding read back through a decoder written against the specification. The sample bitmap the specification works through in section 4.4.1 is reproduced octet for octet, and all eight sides and sheet-back combinations of Table 9 give the transforms the table names. |
| Raster keywords | `PwgRasterTests` | A printer's `pwg-raster-document-type-supported` and `pwg-raster-document-sheet-back` keywords read into the writer's options, and a keyword this library does not know falls back to colour and to `Normal`. |
| Conversion routing | `IppPrinterDocumentFormatTests` | A document is converted only when the printer does not read it, a converter is registered, and that converter writes a format the printer reads. The resolution moves to the nearest one the printer rasters at, the colour space follows the job's colour mode, page ranges reach the converter and not the job template, and a converter that answers with no document or with several fails before anything is sent. |

**What none of them prove** is what happens between the PDF engine and the encoder, because
no Linux machine can render a PDF with the in-box Windows engine. Test 10 below is that gap.

A code review also compared every structure and every constant against the documented
Windows headers: `JOB_INFO_2`, `PRINTER_INFO_2`, `PRINTER_INFO_4`, `DOC_INFO_1`,
`PRINTER_DEFAULTS`, the `PRINTER_STATUS_*` and `JOB_STATUS_*` bits, and the `DC_*` values.
The review found no error. This is a careful reading, not a run on hardware, so the tests
below are still necessary.

## Gather the evidence with the sample

Start the [printer manager sample](samples/printer-manager.md) on the Windows machine, and
open the address it prints:

```
dotnetup dotnet run --project samples/Devices.Samples
```

Open the **Diagnostics** tab, and enter the name of a real, installed print queue under
**The Windows spooler checks**. Press **Gather the evidence**. The checks report the job
list, the printer status bits, the configuration, and the two error paths. Each section is
labelled with the check number below, and each reading is reported next to what that
reading became, for example `Status: Paused (PRINTER_STATUS_PAUSED)`. A person still judges
the result against the checklist; the sample only gathers the evidence to judge.

Check 3, the print cycle, needs the **Also submit the check 3 test job** box, because it
submits a real job. The browser asks for confirmation before it sends anything.

## The tests that run themselves on Windows

`test/Devices.InteropTests` is the only set in this repository that calls `winspool.drv`.
Everything else answers through `IWindowsSpoolerInterop`, and a fake cannot settle whether a
structure is declared correctly: it writes the structure with the same declaration it reads
it with, so a declaration wrong against the real header still round-trips. Only bytes the
spooler itself wrote can tell you.

**They run in CI**, on the `windows` job, against a queue that job creates and pauses. That
settled what nothing else could: `EnumPrinters` returns a queue added in the same session, a
RAW job to a paused Microsoft Print To PDF queue lands in `EnumJobs` with its document name
intact, and reading three of them back confirms `JOB_INFO_2` is declared as the real header
declares it.

The provisioning step throws rather than skips when it cannot make that queue, and the tests
skip themselves unless `DEVICES_TEST_QUEUE` names one. A runner image that stops allowing a
print queue therefore turns the job red instead of quietly proving nothing — which is the
failure this whole arrangement is built around.

Run them by hand as well when you have a real printer. CI proves the declarations against one
runner image and one driver; a driver of your own is the only thing that answers for its own
paper names and its own device mode.

**Microsoft Print to PDF is enough** — no hardware, no driver to install. It has a real
driver, so `DeviceCapabilities` answers with real paper names, and pausing it means nothing
ever renders.

```powershell
# Pause it first. This is not optional: a live queue prints, and Print to PDF stops on a
# Save As dialog that no test can answer.
Get-CimInstance Win32_Printer -Filter "Name='Microsoft Print to PDF'" | Invoke-CimMethod -MethodName Pause
```

```bash
sh ./pipeline/unit-test.sh
```

The script names that queue by default on Windows outside CI. Set `DEVICES_TEST_QUEUE` to use
another one — a real printer works too, as long as it is paused. Every test checks that it is
and refuses otherwise, so a wrong name costs a clear failure and nothing else. Each test
cancels the jobs it sent; leave the queue paused afterwards.

What they settle, which the checklist below used to ask a person for:

| Call | Structure it reads from the spooler | Retires |
| --- | --- | --- |
| `SpoolerPrinterDiscovery.DiscoverAsync` | `PRINTER_INFO_4` | part of test 5 |
| Three jobs, then `GetJobsAsync` | **`JOB_INFO_2`, several entries** | **test 1** |
| `GetStatusAsync` | `PRINTER_INFO_2`, paused | part of test 2 |
| `GetConfigurationAsync` | `DeviceCapabilities` name blocks, `DEVMODEW` | most of test 6 |
| `CancelJobAsync` | `SetJob`, and that 87 really means a job that left | part of test 5 |

None of that needs a person any more. What is left below needs one, and each entry says why.

## The tests to do on Windows

Do these in order. The first is the one that hides the worst kind of error.

### 1. The job list, across more than one job — **done automatically**

~~An error in the `JOB_INFO_2` layout does not show on the first job. It shows on the ones
after it, because each wrong field size moves every later field.~~

`WindowsSpoolerInteropTests.SubmitAndRead_ReadsBackEveryJobTheSpoolerWrote` sends three jobs
with distinct names and reads all three back, on every Windows CI run. Keep the steps below
only if you want a second driver's answer; the layout question itself is settled.

1. Pause a print queue, so the jobs stay in it.
2. Send **three** jobs with different names and different page counts.
3. Run the Windows spooler checks.
4. Look at the **second and the third** job, not only the first. `JobName`,
   `TotalImpressions` and `ImpressionsCompleted` must all be correct.

Wrong values on job two or three, with correct values on job one, mean the structure layout
is wrong.

### 2. The printer status bits, on real hardware

1. Pause the queue. The checks must report `Paused`.
2. Put the printer offline. It must report `Offline`.
3. Cause an error, such as an empty paper tray or an open door. It must report `Error`.
4. Select **Use Printer Offline** in the queue menu. The queue has no
   `PRINTER_STATUS_OFFLINE` bit in this mode; it has the `PRINTER_ATTRIBUTE_WORK_OFFLINE`
   attribute. It must report `Offline`, with `IsAcceptingJobs` false and
   `PRINTER_ATTRIBUTE_WORK_OFFLINE` in the `Detail` text.

In each case, check the `Detail` text too. It must name the bit you expect, for example
`PRINTER_STATUS_PAUSED`, next to the mapped state.

### 3. A full print cycle

Run the checks with the test job box ticked. They send a small `RAW` job through the
complete sequence: `OpenPrinter`, `StartDocPrinter`, `StartPagePrinter`, `WritePrinter`,
`EndPagePrinter`, `EndDocPrinter`, `ClosePrinter`.
- The job must reach the device or the output file.
- The job identifier the scenario prints must agree with the identifier in the Windows
  print queue window.
- The driver calls `WritePrinter` until every byte is written. If `WritePrinter` fails or
  writes zero bytes, the driver throws `InvalidOperationException` with the Win32 error
  text, and it deletes the job with `SetJob(JOB_CONTROL_DELETE)` before `EndDocPrinter`.
  A failed or short write must not leave a partial job in the queue. To check this,
  send a job to a queue whose port is a file that cannot be written (for example a
  read-only path): the call must throw, and the Windows print queue window must show no
  job afterwards.
- Set `MediaType`, `OutputBin`, `PageRanges` and `NumberUp` in the `PrintOptions` of the
  job. The `PrintJobInfo.DroppedOptions` list must name all four, because no `DEVMODE`
  field can carry them, and `JobName` must not be in it.
- Set `Duplex`, `ColorMode`, `Orientation`, `MediaSize` and `MediaSource` to values the
  queue reports, and confirm `DroppedOptions` is empty. Then open the job in the Windows
  print queue window, read its **Properties**, and confirm the values agree. A `MediaSize`
  or `MediaSource` name that the queue never reported must come back in `DroppedOptions`.
- Set `Copies = 3`. The print queue window must show **three** jobs, each with the same
  document name, and the returned `PrintJobInfo.JobId` must be the first of them.
  `PrintJobInfo.Detail` must read `Copy 1 of 3. Each copy is a separate spooler job.`
- Set both `ResolutionDpi` and `Quality`. `DroppedOptions` must name `Quality` only, and
  the job must carry the resolution.
- Send a job with a `DEVMODE` to a queue whose driver reports a short device mode, if you
  have one. The call must throw `InvalidOperationException` naming the reported size,
  instead of writing over the driver-private tail.

### 4. A native AOT publish

A clean `dotnet build -c Release` runs the trim and AOT analyzers, but it **does not prove**
that the marshalling works. The driver uses `Marshal.SizeOf<T>()` and
`Marshal.PtrToStructure<T>()` on structures that hold `string` fields. Only a published AOT
binary proves these behave correctly. The checks cannot do this one; they run as a
normal, non-AOT build.

1. Run `dotnet publish -r win-x64 -p:PublishAot=true`.
2. Call all seven `ISpoolerDriver` methods from the published binary.

### 5. The error paths

The checks run both of these for you.

1. Open a queue name that does not exist. The `InvalidOperationException` must carry a
   sensible Win32 error code and the Windows error text after it, for example
   `OpenPrinter failed with Win32 error 1801: The printer name is invalid.`
2. Cancel a job that already finished, or an id that never existed, on the real queue.
   `CancelJobAsync` must return `false`, not throw. The driver gives
   `ERROR_INVALID_PARAMETER` (87) this special treatment.
3. ~~Call `GetConfigurationAsync` with a queue name that does not exist. `DeviceCapabilities`
   returns `-1`, and the driver must throw `InvalidOperationException`, not return an empty
   configuration.~~ **Covered** by
   `WindowsSpoolerDriverSeamTests.GetConfigurationAsync_ADriverThatRefusesTheQuery_ThrowsRatherThanReportNothing`.
   What is left here is only whether real Windows answers `-1` for a missing queue rather
   than `0`; the two must not read the same, and the test holds that apart.
4. Stop the **Print Spooler** service, then call `EnumeratePrintersAsync` and `GetJobsAsync`.
   Both must throw `InvalidOperationException` with error 1722 (`RPC_S_SERVER_UNAVAILABLE`)
   or a similar code. Neither call may return an empty list. Start the service again.
5. ~~Pass a cancelled `CancellationToken` to any method. The call must throw
   `OperationCanceledException` before it reaches the spooler.~~ **Covered** by
   `WindowsSpoolerDriverSeamTests.EveryEntryPoint_ACancelledToken_ThrowsBeforeItReachesTheSpooler`,
   which asserts it for all eight and that the spooler saw nothing. The token is still
   checked on entry only: a call that already runs inside `winspool.drv` completes on its
   own, and no test on any platform can change that.

### 6. The configuration, against a real driver

Run the checks on a queue that has a real printer driver.

- The paper names print inside brackets, for example `[A4]`. Each name must be a complete
  word, not cut short and not full of stray characters.
- Each paper name carries a Windows paper number, for example `[A4] = 9`. The number must
  look like a `DMPAPER_*` value: `DMPAPER_LETTER` is 1 and `DMPAPER_A4` is 9. A driver with
  its own sizes reports numbers at 256 and above, which is also correct.
- The tray names print inside brackets too, each with its `DMBIN_*` number. A tray name has
  at most 24 characters, so watch for a name that is cut short at that length.
- The three defaults print below the lists: the default paper, the default tray, the
  orientation and the resolution. Each must agree with the **Printing Preferences** dialog
  of that queue. Change a default in the dialog, run the check again, and confirm that the
  reported value follows.
- A default that the device mode does not carry prints as empty. That is correct: the
  driver reports nothing rather than a guess.
- The resolutions must look reasonable for the printer.
- The duplex and colour flags must agree with what the printer can do.

### 7. The access level

The driver asks for `PRINTER_ACCESS_USE`. Confirm this level is enough to submit a job, to
read the status, and to cancel a job with `SetJob`. If your work needs it, also confirm that
a user can cancel a job that another user sent. The checks cannot cover this: they
prove what the level allows, not what a different level would forbid.

### 8. Cleanup after a failed open — **done automatically**

~~Windows does not promise a value for the printer handle when `OpenPrinter` fails.~~
`WindowsSpoolerDriverSeamTests.SubmitAsync_AQueueThatCannotBeOpened_ThrowsAndClosesNothing`
hands the driver a handle it must not keep and asserts `ClosePrinter` is never called, and
`WindowsSpoolerInteropTests.Operations_AQueueThatDoesNotExist_FailWithTheWindowsErrorText`
takes the same path against the real spooler.

## What the driver does not do yet

- **`MediaType`, `OutputBin`, `PageRanges` and `NumberUp` reach nothing.** A `DEVMODE` has
  no field for a page range, for pages per sheet or for an output bin, and `dmMediaType`
  needs a `DMMEDIA_*` number that the spooler does not pair with a name. The driver always
  names these four in `PrintJobInfo.DroppedOptions`.
- **A short device mode is refused, not worked around.** `SubmitAsync` throws when a driver
  reports a device mode smaller than `DEVMODEW`, because writing the fields back would
  overwrite the driver-private tail behind it. No driver in use reports one.
- **`GetJobAsync` lists the jobs and then selects one.** The `GetJobW` entry point can fetch
  one job directly. The current code is correct but does more work than it must.

## Still to check by hand: the device identity of a queue

`WindowsSpoolerDriver.GetIdentityAsync` reads `PRINTER_INFO_2.pPortName` through
`OpenPrinter` and `GetPrinter`, and `SpoolerAliases.FromPortName` turns it into the key
that joins a queue to the device behind it. Neither has run on Windows.

The enumeration deliberately stays at level 4. `EnumPrinters` at level 2 opens every
remote connection over RPC, so one unreachable print server would stall the whole
discovery until the call times out. The port name is therefore read one queue at a time,
and only when the caller sets `PrinterManagerOptions.ReadIdentity`.

The seven port forms below are covered by `SpoolerAliasesTests`, which reads each one —
`IP_<address>`, a renamed port, `USB001`, `WSD-…`, `\\server\queue`, and the first port of
a pool. What is left for a person is only whether Windows produces those spellings, and
whether `IsDefault` and `IsShared` agree with the printer settings.

~~Check on a real Windows machine:~~

1. A queue on a standard TCP/IP port. `ReadIdentity` should put the queue and the
   `raw://<address>` channel of the same printer on **one** `PrinterDevice`.
2. A queue on a `USB001` port. It should stay its own device: the port names no device.
3. A queue on a Web Services port (`WSD-…`). It should stay its own device.
4. A queue connected to another server (`\\server\queue`). It should stay its own device.
5. A queue whose port was renamed away from the `IP_<address>` form. Nothing should be
   guessed from the new name unless the name is itself an address.
6. A print pool with several ports. The first port should be the one that is read.
7. A queue whose server is switched off. `ReadIdentity` should report the other printers
   normally and simply say nothing about this one.

Also confirm that `PrinterInfo.IsDefault` and `PrinterInfo.IsShared`, which the same
level 2 read can fill, match what the Windows printer settings show.

### 9. A PDF through the spooler

The `queue-sweep` job set prints `document.pdf` through the spooler queue. It needs the
`AdaptArch.Devices.Windows` package with `WindowsPrinting.EnablePdfPrinting()`
(the sample calls it on Windows); without that call the job fails with
`NotSupportedException` before anything spools.

- ~~Every page of the PDF must print, in order, as one job in the queue window.~~ **Done**:
  the single-page `document.pdf` printed in the five-job sweep. `pages.pdf` is in the set now
  and re-checks this across four pages.
- A multi-page PDF with `PageRanges` must print only the selected pages. `pages.pdf` with
  `2,4` is in the set.
- A corrupt PDF must fail with `InvalidOperationException` naming the file, and leave no job
  in the queue. `corrupt.pdf` is in `PrintFiles` for this: it is `pages.pdf` truncated, so it
  keeps a valid `%PDF-1.7` header and has no xref and no trailer. The header matters — a file
  that did not look like a PDF at all would be refused earlier, by something other than the
  engine, and would prove nothing about this path.

  It is **not** in any job set, because a job that always fails would make every sweep report
  a failure. Send it by itself from the sample. The conversion happens before anything is
  spooled, so the queue window must stay empty: a job that appears and then disappears is a
  different bug from no job at all, and only the queue window tells them apart.

### 10. A PDF rendered to PWG Raster, over IPP

The path no automated test can reach. The encoder is tested on Linux, but nothing there can
render a PDF, so the pixels that reach the encoder have never been seen.

Run the same `queue-sweep` set against an `ipp://` or `ipps://` printer whose
`document-format-supported` lists `image/pwg-raster` and **not** `application/pdf`. The four
`pages.pdf` jobs are the ones that matter, and each page carries the marks that name its own
fault.

Check the log first. Event 1032 must say the job was converted, and name `image/pwg-raster`.
If 1034 appears instead nothing was converted and the rest of this section proves nothing;
the reason it carries says which of the four conditions failed.

**The row stride.** `GetPixelDataAsync` is trusted to answer with tightly packed rows, which
a locked buffer does not promise — that is why it is not used. If a row carries padding, the
encoder reads it as picture and every line starts a little further along than the last.

- The vertical grid lines must stay vertical and evenly spaced. A stride fault leans them,
  and the lean grows down the page.
- The diagonal must stay straight, corner to corner.
- Nothing may repeat or smear along one edge.
- **Both the colour job and the grayscale job must be right.** They are three octets a pixel
  and one octet a pixel, so a stride fault usually shows in one and not the other. A clean
  colour page beside a sheared grey one is the signature.

**The duplex back side.** PWG 5102.4 Table 9 gives the transform for the back of a sheet, and
it depends on the printer's `pwg-raster-document-sheet-back` as well as on the edge the sheet
turns on. Get it wrong and every second page is upside down or mirrored, with no error.

- On the duplex job, hold a sheet as you would read the front and turn it over on the long
  edge. Page 2 must read the right way up, `TOP OF PAGE` at the top.
- The grey corner block must be in the **top right** on both sides. Top left means the page
  was flipped where it should have been rotated; bottom right means it was rotated twice.
- The page numbers must run 1, 2, 3, 4 across the two sheets.
- Write down what the printer reported for `pwg-raster-document-sheet-back`. The transform is
  only right for that value, so a second printer reporting another one is worth the run.

**The rest.** Quick, and each fails visibly.

- The page must be upright and the right size, not stretched or squashed.
- Grayscale must be grey and not inverted: the page is mostly white with black lines.
- The page-range job must print pages 2 and 4 of the document, not pages 2 and 4 of a
  document already cut to two pages. The converter selects the pages, so the job template
  must carry no `page-ranges` afterwards; a printer that applied them twice prints page 4
  alone, or nothing.
- Four pages must arrive as **one** job in the printer's queue, not four.
- `corrupt.pdf` must fail here too, and no job may reach the printer. The renderer runs
  before the IPP request is built, so the failure is the same `InvalidOperationException`
  as on the spooler — but it is worth confirming on this path as well, because it is the
  path where a half-written document would be sent rather than dropped.

### 11. Placement, on the stock the customer uses

**Why by hand.** The fit, the anchor and the offset are arithmetic with unit tests, and the
composition has its own. What no test on any machine can tell you is whether the page came out
where a ruler says it should: a label a millimetre out is a rejected parcel, and the only
instrument for that is a ruler on the real stock.

Print `PrintJobs/pdf-placement.json` through a spooler queue, and the same set through an IPP
queue if one is reachable. The two paths place a page by different means — GDI draws it on
Windows, a composed raster carries it over IPP — so agreeing on paper is the thing worth
proving.

- **The reference sheet** is the first job, centred. Measure the margins on all four sides;
  they are what every other sheet is compared against.
- **The anchored sheet** must sit against the top left corner of the printable area, with the
  two margins there as small as the printer allows and the slack on the other two sides.
- **The offset sheet** must be exactly 5 mm right and 3 mm down of the anchored one. Measure
  it, do not judge it. This is the number a customer will send you when it is wrong.
- **The bottom right sheet** proves the anchor is a corner and not a direction: the same
  offset now pushes the page off the stock, and what fits must print with no error.
- **The smoothing sheet** must show hard bar edges. Scan the barcode with a real scanner, at
  the distance an operator would. Compare against the job before it.
- **The document media sheet** must print the page at exactly the size the PDF declares. This
  is the one where a wrong answer is obvious with a ruler: an A4 page must measure 210 by
  297 mm.
- **The custom media sheet** asks for 100 by 150 mm. Confirm the driver took it, and write
  down what the queue reported afterwards. On a queue whose driver refuses custom sizes the
  option is reported in `DroppedOptions`, which is the right answer and not a failure.

Write down the printer, the stock and every measurement. A sheet without its measurements
proves nothing a photograph would not.
