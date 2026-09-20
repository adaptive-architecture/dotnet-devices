# The printer manager sample

`samples/Devices.Samples` is a small web application: a printer manager the whole library
can be driven from. Start it, and open the address it prints.

```bash
dotnetup dotnet run --project samples/Devices.Samples
```

It listens on `http://localhost:5080` and on the loopback address only, because it prints
to real hardware. Set `ASPNETCORE_URLS` to move it, and know what that means.

The page is the work on the left and the log on the right. The printer and its channel sit
above the tabs and not inside one, in that order, because one owns the other and because
printing, running a job set and reading a status all send to them. Nothing is selected until
a person selects it: a button that needs a channel says so while none is, and the tabs that
need no printer say that too.

The four tabs are:

- **Print** — the file and the options, then one button. The file is one the sample ships
  in `PrintFiles/`, or one uploaded from the machine. A switch sends the bytes unchanged
  (raw) instead of through the queue. The options form offers only what the channel
  reported, so the page never invents a choice. Before the button, the page calls
  `PrinterDevice.Accepts` and warns when the channel says it does not read the format.
- **Job sets** — the scripted hardware test. A job set is a JSON file; the sample ships
  `PrintJobs/queue-sweep.json` (five documents through one queue, each changing one option
  from the job before it) and `PrintJobs/raw-sweep.json` (a JPEG, a ZPL label and an EPL
  label, unchanged). A set can also be uploaded, so the same test runs on Windows, Linux and
  macOS and the results compare job by job. The run ends with a summary of what became of
  each job. The set is edited in the browser: one tab per job, named by the file it prints,
  with the same option controls the print tab builds from what the channel reported, and jobs
  that can be added, duplicated and removed. The edit is a copy — the file on disk is untouched, and **Download** writes
  what the form holds, so a set is written without hand-editing JSON.
- **Printer** — the status and the capabilities of the selected device, channel by channel.
- **Diagnostics** — which channels reach one queue (`QueueCorrelation`, with an optional
  tracer job), what one host answers over IPP and over SNMP, and the Windows spooler checks
  of [windows-manual-tests.md](../windows-manual-tests.md).

A job reports its progress over minutes, so every flow that prints answers with a stream of
server-sent events. The browser shows each line as it arrives in a log rail beside the form,
which can be copied, downloaded, filtered to the warnings or the errors, and hidden with the
**Log** button beside **Probe subnet**. What arrives while it is hidden is counted on that
button, and every action also reports its outcome beside the button that started it, so a
hidden log never reads as nothing happening. Closing the page stops the watch. The printer
keeps the job.

The console the sample itself writes keeps the framework quiet: ASP.NET Core is filtered to
its warnings, so what scrolls past is the address, and then what the library says.

## The job set format

```json
{
  "name": "Queue sweep",
  "description": "Each job changes one option from the job before it.",
  "mode": "queue",
  "jobs": [
    { "file": "document.pdf", "description": "a PDF with the printer defaults" },
    { "file": "image.png", "description": "a PNG in colour, at its own size",
      "options": { "colorMode": "Color", "scaling": "None" } }
  ]
}
```

`mode` is `queue` or `raw`. `options` holds the properties of `PrintOptions`, with an enum
written as its name and `pageRanges` written as `"1-3,5"`. `contentType` overrides what the
file extension says. The target printer is not in the set: the printer is what differs
between two machines, so the person selects it in the browser.

`converter` names the engine that renders a document, where the build registered more than
one. It maps to `PrintOptions.ConverterName`, which also forces the conversion: an IPP printer
that reads PDF would otherwise be sent the document untouched and neither engine would run.
The shipped **PDF engines** set uses it to print the same two jobs through each engine in turn:

```json
{ "file": "pages.pdf",
  "description": "PDFium: pages 1 and 3 in colour, duplex on the long edge",
  "options": { "converter": "PDFium", "pageRanges": "1,3", "colorMode": "Color", "duplex": "LongEdge" } }
```

Which names exist depends on the packages the build referenced, so the page reads them from
`GET /api/pdf-engines` rather than offering a fixed list, and shows the selector only where
there is a choice to make. On Linux and macOS that is PDFium alone, and the jobs of that set
which name `Windows` fail with a message listing what does exist. To see the same pages
without printing them, run the tests and open `artifacts/rasterization/index.html`.

## The HTTP interface

| Method and path | What it does |
| --- | --- |
| `GET /api/printers` | The devices and their channels. `?refresh=true` browses again; `?probe=true` also probes the local subnet. |
| `GET /api/printers/status?id=` | The status of one printer. |
| `GET /api/printers/accepts?id=&contentType=` | The tri-state answer of `PrinterDevice.Accepts`. |
| `GET /api/files` | The files in `PrintFiles/`. |
| `GET /api/pdf-engines` | The PDF engines this process registered, best first. `isDefault` marks the one a job that names none will get. |
| `POST /api/jobs` | Print a file from `PrintFiles/`. Answers with server-sent events. |
| `POST /api/jobs/upload` | The same, with the bytes in a multipart form. |
| `GET /api/job-sets` | The job sets in `PrintJobs/`. |
| `POST /api/job-sets/run` | Run a job set. The set is in the body, so an uploaded set and a supplied set take one path. |
| `POST /api/diagnostics/correlate` | Compare the job queues. `{"tracer": true}` permits a tracer job. |
| `GET /api/diagnostics/details?host=` | What one host answers over IPP and over SNMP. |
| `POST /api/diagnostics/windows-spooler` | The Windows spooler checks. |

Nothing prints until a `POST` arrives, and the browser asks the person first.

## Building with trimming and native AOT

```bash
sh ./pipeline/publish-samples.sh -r linux-x64
```

The script publishes the sample three times — framework-dependent, trimmed self-contained,
and native AOT — into `./artifacts/samples/<rid>/`. Warnings stay errors, so an `IL2xxx` trim
warning or an `IL3xxx` AOT warning fails the publish. This is how a trim problem in the
library is found early. Use `-o` to select another output directory.

The web host is built for this. `WebApplication.CreateSlimBuilder` leaves out what a printer
manager never uses, every contract is serialized through a source-generated
`JsonSerializerContext`, and the project sets `EnableRequestDelegateGenerator`, which the SDK
otherwise turns on only for a trimmed or a native AOT publish. With it on, an endpoint shape
the generator cannot read fails an ordinary build instead of the publish at the end of the
day.

The sample takes no NuGet dependency for the web host: `Microsoft.NET.Sdk.Web` adds a
framework reference and nothing else. A trimmed publish turns reflection-based JSON off
(`JsonSerializerIsReflectionEnabledByDefault=false`), so a contract missing from the context
fails at start-up, where the endpoint is mapped.

[Development](../development.md#trim-and-native-aot) says why this publish is the library's
trim and AOT gate.
