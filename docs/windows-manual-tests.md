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
| Printer status bits | `WindowsSpoolerStatusMapperTests` | Each `PRINTER_STATUS_*` bit maps to the correct `PrinterStatusState`, and the precedence is correct when two bits are set together. |
| Job status bits | `WindowsSpoolerStatusMapperTests` | Each `JOB_STATUS_*` bit maps to the correct `PrintJobState`, with the same precedence check. |
| Paper names | `WindowsSpoolerCapabilityParserTests` | `DC_PAPERNAMES` gives fixed 64-character blocks. The parser reads a short name with null padding, a name that fills all 64 characters with no terminator, an empty block, and several blocks in sequence. |
| Resolutions | `WindowsSpoolerCapabilityParserTests` | `DC_ENUMRESOLUTIONS` gives pairs of integers. The parser reads a list of pairs, one pair, and an empty buffer. |
| Platform guard | `WindowsSpoolerDriverTests` | All seven `ISpoolerDriver` methods throw `PlatformNotSupportedException` on a machine that is not Windows, before any native call. |
| Driver selection | `SpoolerDriverFactoryTests` | The factory gives a CUPS driver on Linux and macOS. |

A code review also compared every structure and every constant against the documented
Windows headers: `JOB_INFO_2`, `PRINTER_INFO_2`, `PRINTER_INFO_4`, `DOC_INFO_1`,
`PRINTER_DEFAULTS`, the `PRINTER_STATUS_*` and `JOB_STATUS_*` bits, and the `DC_*` values.
The review found no error. This is a careful reading, not a run on hardware, so the tests
below are still necessary.

## The tests to do on Windows

Do these in order. The first is the one that hides the worst kind of error.

### 1. The job list, across more than one job

An error in the `JOB_INFO_2` layout does not show on the first job. It shows on the ones
after it, because each wrong field size moves every later field.

1. Pause a print queue, so the jobs stay in it.
2. Send **three** jobs with different names and different page counts.
3. Call `GetJobsAsync`.
4. Look at the **second and the third** job, not only the first. `JobName`,
   `TotalImpressions` and `ImpressionsCompleted` must all be correct.

Wrong values on job two or three, with correct values on job one, mean the structure layout
is wrong.

### 2. The printer status bits, on real hardware

1. Pause the queue. `GetStatusAsync` must report `Paused`.
2. Put the printer offline. It must report `Offline`.
3. Cause an error, such as an empty paper tray or an open door. It must report `Error`.

### 3. A full print cycle

Send a real `RAW` job, such as ZPL or ESC/POS, through the complete sequence: `OpenPrinter`,
`StartDocPrinter`, `StartPagePrinter`, `WritePrinter`, `EndPagePrinter`, `EndDocPrinter`,
`ClosePrinter`.

- The job must reach the device or the output file.
- The job identifier that comes back must agree with the identifier in the Windows print
  queue window.
- The driver now checks the byte count itself: if `WritePrinter` writes fewer bytes than
  the payload holds, the driver throws `InvalidOperationException` and names both counts.
  You do not need to check this by hand.

### 4. A native AOT publish

A clean `dotnet build -c Release` runs the trim and AOT analyzers, but it **does not prove**
that the marshalling works. The driver uses `Marshal.SizeOf<T>()` and
`Marshal.PtrToStructure<T>()` on structures that hold `string` fields. Only a published AOT
binary proves these behave correctly.

1. Run `dotnet publish -r win-x64 -p:PublishAot=true`.
2. Call all seven `ISpoolerDriver` methods from the published binary.

### 5. The error paths

1. Open a queue name that does not exist. The `InvalidOperationException` must carry a
   sensible Win32 error code.
2. Cancel a job that already finished. `CancelJobAsync` must return `false`, not throw.
   The driver gives `ERROR_INVALID_PARAMETER` (87) this special treatment.

### 6. The configuration, against a real driver

Call `GetConfigurationAsync` on a queue that has a real printer driver.

- The paper names must be complete words, not cut short and not full of stray characters.
- The resolutions must look reasonable for the printer.
- The duplex and colour flags must agree with what the printer can do.

### 7. The access level

The driver asks for `PRINTER_ACCESS_USE`. Confirm this level is enough to submit a job, to
read the status, and to cancel a job with `SetJob`. If your work needs it, also confirm that
a user can cancel a job that another user sent.

### 8. Cleanup after a failed open

Windows does not promise a value for the printer handle when `OpenPrinter` fails. Confirm
that a failed open, such as a queue name that does not exist, does not cause a problem in
the cleanup that follows. This is a cheap check and the risk is low.

## What the driver does not do yet

- **Only `JobName` reaches the spooler.** `PrintOptions.Copies`, `Duplex`, `ColorMode`,
  `Orientation`, `MediaSource`, `MediaSize` and `ResolutionDpi` are **not** mapped into a
  `DEVMODE`. A `RAW` job goes to the device unchanged, so these options change nothing
  today. This is a known gap, recorded in a comment in `SubmitAsync`.
- **`GetJobAsync` lists the jobs and then selects one.** The `GetJobW` entry point can fetch
  one job directly. The current code is correct but does more work than it must.
