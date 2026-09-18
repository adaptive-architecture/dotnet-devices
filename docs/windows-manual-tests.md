# Windows Manual Tests

The Windows spooler driver speaks to `winspool.drv` through native interop. This code
**cannot run** in the usual development environment: the repository is developed on Linux
and the CI workflow uses `ubuntu-latest` only. No automated test executes a single
`winspool.drv` call.

This page lists what a person must test on a real Windows machine, and what the automated
tests already prove.

## What the automated tests already prove

These tests run in the usual suite, on Linux, and they need no Windows machine. The driver
keeps the native calls apart from the logic that reads their results, so the logic is
testable.

| Area | Test class | What it proves |
| --- | --- | --- |
| Printer status bits | `WindowsSpoolerStatusMapperTests` | Each `PRINTER_STATUS_*` bit maps to the correct `PrinterStatusState`, the precedence is correct when two bits are set together, and each bit is named correctly in `PrinterStatus.Detail`. The `PRINTER_ATTRIBUTE_WORK_OFFLINE` attribute maps to `Offline`. |
| Device mode options | `WindowsSpoolerDeviceModeMapperTests` | Each option maps to the correct `DEVMODE` field and `DM_*` bit, a media or tray name is looked up in the numbers the queue reported, a resolution wins over a quality on `dmPrintQuality`, and every option that reached no field is named in `DeviceModeRequest.Dropped`, so `PrintJobInfo.DroppedOptions` is correct. |
| Option support per channel | `PrinterSchemesTests` | `SupportedOptions` names what a Windows device mode carries, and everything the library models on CUPS and IPP. |
| Job status bits | `WindowsSpoolerStatusMapperTests` | Each `JOB_STATUS_*` bit maps to the correct `PrintJobState`, with the same precedence check. |
| Paper names | `WindowsSpoolerCapabilityParserTests` | `DC_PAPERNAMES` gives fixed 64-character blocks. The parser reads a short name with null padding, a name that fills all 64 characters with no terminator, an empty block, and several blocks in sequence. |
| Resolutions | `WindowsSpoolerCapabilityParserTests` | `DC_ENUMRESOLUTIONS` gives pairs of integers. The parser reads a list of pairs, one pair, and an empty buffer. |
| Platform guard | `WindowsSpoolerDriverTests` | All seven `ISpoolerDriver` methods throw `PlatformNotSupportedException` on a machine that is not Windows, before any native call. |
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

Start the sample on the Windows machine, and open the address it prints:

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

## The tests to do on Windows

Do these in order. The first is the one that hides the worst kind of error.

### 1. The job list, across more than one job

An error in the `JOB_INFO_2` layout does not show on the first job. It shows on the ones
after it, because each wrong field size moves every later field.

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
3. Call `GetConfigurationAsync` with a queue name that does not exist. `DeviceCapabilities`
   returns `-1`, and the driver must throw `InvalidOperationException`, not return an empty
   configuration.
4. Stop the **Print Spooler** service, then call `EnumeratePrintersAsync` and `GetJobsAsync`.
   Both must throw `InvalidOperationException` with error 1722 (`RPC_S_SERVER_UNAVAILABLE`)
   or a similar code. Neither call may return an empty list. Start the service again.
5. Pass a cancelled `CancellationToken` to any method. The call must throw
   `OperationCanceledException` before it reaches the spooler. The token is checked on
   entry only: a call that already runs inside `winspool.drv` completes on its own.

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

### 8. Cleanup after a failed open

Windows does not promise a value for the printer handle when `OpenPrinter` fails. Confirm
that a failed open, such as a queue name that does not exist, does not cause a problem in
the cleanup that follows. The checks exercise this path in check 5, when they open
a queue name that cannot exist; confirm the process itself stays healthy afterwards.

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

Check on a real Windows machine:

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

- Every page of the PDF must print, in order, as one job in the queue window.
- A multi-page PDF with `PageRanges` must print only the selected pages.
- A password-protected or corrupt PDF must fail with `InvalidOperationException`
  naming the file, and leave no job in the queue.

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
