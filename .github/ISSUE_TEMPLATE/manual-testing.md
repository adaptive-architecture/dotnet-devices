---
name: Manual testing before a release
about: Run the smoke job sets on real printers on Windows, Linux and macOS
title: "Manual testing for <version>"
labels: manual-testing
---

**Commit under test:** <!-- sha or tag -->

The two smoke sets cover the paths no unit test can, on as little paper as possible. The
[smoke run](../blob/main/docs/samples/printer-manager.md#the-smoke-run) explains each set,
and every job's description says what to measure or look for.

## Printers

- An office printer with a duplex unit and more than one tray, reachable as a Windows spooler
  queue and as a CUPS queue on Linux and macOS: runs `smoke-spooler`. 5 sheets per platform.
- A ZPL label printer that also reads EPL, on 4 by 6 inch labels: runs `smoke-zpl`. 6 labels
  per channel at most.

## Start the sample on each platform

```bash
dotnetup dotnet run --project samples/Devices.Samples
```

Open `http://localhost:5080`, choose the printer and its channel above the tabs, then open
**Job sets**.

## Set the tray and paper names (Windows 11)

Job 4 of `smoke-spooler` (`4 · pages.pdf`) prints from a named tray on named paper. The file
holds the IPP names (`tray-2`, `iso_a4_210x297mm`), which a CUPS queue reports. A Windows
driver reports its own names, such as `Tray 2` and `A4`, so the job must be edited before it
runs:

1. Choose the office printer's spooler channel, then `smoke-spooler` under **Start from**.
2. Open the job tab `4 · pages.pdf`.
3. **Tray** shows `tray-2 (not reported by this channel)`. Choose a tray other than the
   printer default from the list, and load A4 in it.
4. **Media** shows `iso_a4_210x297mm (not reported by this channel)`. Choose `A4`, or the
   driver's name for it.
5. Run the set from this page. The edit lives in the browser only; **Download** saves it as a
   file, and **Reset to the shipped set** discards it.

If either name is not changed, the job still prints, and the log reports the option as
dropped. Do the same on Linux and macOS if either list shows `(not reported by this channel)`.

## Run the sets

Run `smoke-spooler` first: its four one-sided jobs print first, then one duplex job on fresh
paper. Each set has one job that must fail and print nothing.

After each submission the log names the engine that rendered the job and the format sent.
Write both down with every dropped option, then save the log with **Download** in the log rail
and attach it here.

### Windows

- [ ] Tray and paper names set on job 4, as above
- [ ] `smoke-spooler` on the office printer's spooler queue
- [ ] `smoke-zpl` on the label printer's spooler queue
- [ ] `smoke-zpl` on the label printer's IPP channel and raw socket, where it has them
- [ ] Logs attached

### Linux

- [ ] `smoke-spooler` on the office printer's CUPS queue
- [ ] `smoke-zpl` on the label printer's CUPS queue
- [ ] `smoke-zpl` on the label printer's IPP channel and raw socket, where it has them
- [ ] Logs attached

### macOS

- [ ] `smoke-spooler` on the office printer's CUPS queue
- [ ] `smoke-zpl` on the label printer's CUPS queue
- [ ] `smoke-zpl` on the label printer's IPP channel and raw socket, where it has them
- [ ] Logs attached

## Results

<!-- One line per job that did not do what its description says: platform, set, job number,
what happened. -->
