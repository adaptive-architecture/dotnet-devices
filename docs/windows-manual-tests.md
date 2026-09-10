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
| Unapplied options | `WindowsSpoolerDriverTests` | `UnappliedOptions` names every set `PrintOptions` property except `JobName`, so `PrintJobInfo.DroppedOptions` is correct. |
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

## Gather the evidence with the sample

Run this on the Windows machine, from the `samples/Devices.Samples` directory:

```
dotnet run -- win-printer-test <queue-name>
```

Pass the name of a real, installed print queue. The scenario prints the job list, the
printer status bits, the configuration, and the two error paths. It labels each section
with the check number below, and it prints what it read next to what that reading became,
for example `Status: Paused  (PRINTER_STATUS_PAUSED)`. A person still judges the result
against the checklist; the command only gathers the evidence to judge.

Check 3, the print cycle, needs the `--print` switch, because it submits a real job:

```
dotnet run -- win-printer-test <queue-name> --print
```

The scenario asks for confirmation before it sends anything.

## The tests to do on Windows

Do these in order. The first is the one that hides the worst kind of error.

### 1. The job list, across more than one job

An error in the `JOB_INFO_2` layout does not show on the first job. It shows on the ones
after it, because each wrong field size moves every later field.

1. Pause a print queue, so the jobs stay in it.
2. Send **three** jobs with different names and different page counts.
3. Run `win-printer-test`.
4. Look at the **second and the third** job, not only the first. `JobName`,
   `TotalImpressions` and `ImpressionsCompleted` must all be correct.

Wrong values on job two or three, with correct values on job one, mean the structure layout
is wrong.

### 2. The printer status bits, on real hardware

1. Pause the queue. `win-printer-test` must report `Paused`.
2. Put the printer offline. It must report `Offline`.
3. Cause an error, such as an empty paper tray or an open door. It must report `Error`.
4. Select **Use Printer Offline** in the queue menu. The queue has no
   `PRINTER_STATUS_OFFLINE` bit in this mode; it has the `PRINTER_ATTRIBUTE_WORK_OFFLINE`
   attribute. It must report `Offline`, with `IsAcceptingJobs` false and
   `PRINTER_ATTRIBUTE_WORK_OFFLINE` in the `Detail` text.

In each case, check the `Detail` text too. It must name the bit you expect, for example
`PRINTER_STATUS_PAUSED`, next to the mapped state.

### 3. A full print cycle

Run `win-printer-test <queue-name> --print`. It sends a small `RAW` job through the
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
- Set `Copies`, `Duplex` and `MediaSize` in the `PrintOptions` of the job. The
  `PrintJobInfo.DroppedOptions` list must name all three, and `JobName` must not be in it.

### 4. A native AOT publish

A clean `dotnet build -c Release` runs the trim and AOT analyzers, but it **does not prove**
that the marshalling works. The driver uses `Marshal.SizeOf<T>()` and
`Marshal.PtrToStructure<T>()` on structures that hold `string` fields. Only a published AOT
binary proves these behave correctly. `win-printer-test` cannot do this check; it runs as a
normal, non-AOT build.

1. Run `dotnet publish -r win-x64 -p:PublishAot=true`.
2. Call all seven `ISpoolerDriver` methods from the published binary.

### 5. The error paths

`win-printer-test` runs both of these for you.

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

Run `win-printer-test` on a queue that has a real printer driver.

- The paper names print inside brackets, for example `[A4]`. Each name must be a complete
  word, not cut short and not full of stray characters.
- The resolutions must look reasonable for the printer.
- The duplex and colour flags must agree with what the printer can do.

### 7. The access level

The driver asks for `PRINTER_ACCESS_USE`. Confirm this level is enough to submit a job, to
read the status, and to cancel a job with `SetJob`. If your work needs it, also confirm that
a user can cancel a job that another user sent. `win-printer-test` cannot check this: it
proves what the level allows, not what a different level would forbid.

### 8. Cleanup after a failed open

Windows does not promise a value for the printer handle when `OpenPrinter` fails. Confirm
that a failed open, such as a queue name that does not exist, does not cause a problem in
the cleanup that follows. `win-printer-test` exercises this path in check 5, when it opens
a queue name that cannot exist; confirm the process itself stays healthy afterwards.

## What the driver does not do yet

- **Only `JobName` reaches the spooler.** `PrintOptions.Copies`, `Duplex`, `ColorMode`,
  `Orientation`, `MediaSource`, `MediaSize` and `ResolutionDpi` are **not** mapped into a
  `DEVMODE`. A `RAW` job goes to the device unchanged, so these options change nothing
  today. This is a known gap, recorded in a comment in `SubmitAsync`.
- **`GetJobAsync` lists the jobs and then selects one.** The `GetJobW` entry point can fetch
  one job directly. The current code is correct but does more work than it must.
