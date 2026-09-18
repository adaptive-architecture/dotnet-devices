# Packages

`dotnet-devices` is distributed as NuGet packages under the `AdaptArch.` prefix. Packages are built from `src/` projects.

## NuGet Packages

| Package | Description |
| :--- | :--- |
| `AdaptArch.Devices` | Core cross-platform device abstractions (printers, scanners, peripherals). Four reviewed runtime dependencies, listed below. |
| `AdaptArch.Devices.DependencyInjection` | `Microsoft.Extensions.DependencyInjection` registrations for `AdaptArch.Devices` (`AddDevices`, `AddPrinters`). |
| `AdaptArch.Devices.Windows` | Windows-only extensions for `AdaptArch.Devices`: spooler PDF rendering through the in-box engine. No runtime NuGet dependency of its own. |

Framework integrations (DI/hosting/logging) ship as separate packages, so consumers take only
what they use.

## Runtime Dependencies

A dependency in a core package is inherited by every consumer, so **each one needs explicit
review and sign-off before it is added**. [AGENTS.md](../AGENTS.md) states the tests a
candidate must pass. This page records what was approved, and why.

Approved dependencies of `AdaptArch.Devices`:

| Package | Licence | Transitive deps | Why it was approved |
| :--- | :--- | :--- | :--- |
| `SharpIppNext` | MIT | None on `net10.0` | A complete IPP 1.1 implementation. It replaces a hand-written IPP encoder and decoder, states native AOT support with zero reflection, and opens the way to job submission. |
| `Lextm.SharpSnmpLib` | MIT | None | A maintained SNMP implementation. It replaces a hand-written BER encoder and decoder. Taken at `13.0.0-beta.3`; see the exception below. |
| `Makaretu.Dns.New` | MIT | None | A DNS data model with a wire-format reader and writer. It replaces a hand-written DNS codec, including the name decompression that was the highest risk in the mDNS browse. |
| `Microsoft.Extensions.Logging.Abstractions` | MIT | `Microsoft.Extensions.DependencyInjection.Abstractions` | The logging seam of .NET. Approved in [issue 10](https://github.com/adaptive-architecture/dotnet-devices/issues/10): a stopped print job could not be diagnosed, because the library wrote no log at all. Microsoft maintains it, it supports `net10.0`, and its `LoggerMessage` source generator uses no reflection, so the package stays trim-safe and AOT-safe. It is an abstraction package, not an implementation: an application that registers no logger pays for `NullLogger` only. |

The first three libraries are not exposed in the public API surface: each one stays behind a
client class, so any of them can be replaced without a breaking change.

### Approved exception: the logging abstraction is in the public API

`Microsoft.Extensions.Logging.Abstractions` breaks the rule above, on purpose. The
`LoggerFactory` members of `IppTransportOptions`, of `PrinterManagerOptions` and of each
printing type a caller can build by itself are all `ILoggerFactory`, and they are the only
members of this library that name a type of the package. The reason is that `ILoggerFactory`
**is** the seam a .NET application
already holds. A wrapper of our own would make every consumer write an adapter to give the
library the logger it already has, which is the cost this dependency was taken to remove.
The type is an abstraction that Microsoft keeps stable, so it does not tie the library to
one logging implementation.

Read [Troubleshooting](troubleshooting.md) for what the log reports and how to turn it on.

PWG Raster adds no package either. `PwgRasterWriter` in the core package writes PWG 5102.4
by hand: a 1796-octet page header and a PackBits-like run-length encoding, which is a few
hundred lines of managed code with no reflection and no platform call. It is public, so an
application that already has a rasterizer can write a conforming document without one. What
stays per-platform is turning a document into pixels, and on Windows that is the in-box
engine the package below already uses.

Windows image printing deliberately adds no package: PNG and JPEG jobs are drawn with
GDI+ through `gdi32.dll` and `gdiplus.dll`, which are system components, so there is
nothing to review.

### Windows-only package instead of a Windows-only dependency

PDF on the Windows spooler renders with `Windows.Data.Pdf`, which only a `-windows`
target can see. Referencing the Windows SDK targeting pack from the cross-platform
core is not supported (restore fails with NU1213, and suppressing that warning was
rejected), so the renderer lives in `AdaptArch.Devices.Windows`
(`net10.0-windows10.0.19041.0`) instead. That package brings no runtime NuGet
dependency of its own: the `-windows` TFM resolves the SDK projection implicitly,
and the calls only run on Windows 10 and later, where the engine ships in-box. The
core package keeps no Windows SDK reference and stays dependency-free; the Windows
package supplies one public `IPrintPayloadConverter`
(`WindowsPrinting.PdfConverter`), which an application registers through
`PrinterManagerOptions.Converters` or, for the whole process, with
`WindowsPrinting.EnablePdfPrinting()`. Without a converter a PDF job fails
with `NotSupportedException` before anything spools. The seam is an interface the
application implements, so both sides stay trim- and AOT-safe with no reflection,
and the same seam carries any other format (see
[printers.md](printers.md#add-a-format-the-library-does-not-know)).

### Approved exception: a prerelease SNMP dependency

`Lextm.SharpSnmpLib` is taken at **`13.0.0-beta.3`**, a prerelease. This is a deliberate
exception, approved because version 12 cannot meet the trim and AOT test above:

- Version 12.5.7 marks every send and parse path with `RequiresUnreferencedCode`
  (16 members). `src/` sets `IsAotCompatible` and turns warnings into errors, so those
  members fail the build.
- Version 13 is rebuilt on `System.Formats.Asn1` and carries **no** such annotation, so it
  is the only version that keeps this package trim-safe and AOT-safe.

The exception costs one suppression: a stable package that depends on a prerelease raises
NU5104, so `src/Devices/Devices.csproj` sets `<NoWarn>$(NoWarn);NU5104</NoWarn>`, scoped to
that project and commented there. **Remove it when 13.x reaches a stable release.**

Consumers inherit the prerelease dependency. Verified with a trimmed publish that calls all
three clients: no IL warnings, and the trimmed binary read a real printer over mDNS, SNMP
and IPP.

`Makaretu.Dns.New` was chosen over every library that does a complete mDNS browse, because
each of those fails a test: `Zeroconf` depends on `System.Reactive`,
`Makaretu.Dns.Multicast.New` on `Common.Logging`, `DNS` (kapetan, last released 2021) on
`System.Reflection.TypeExtensions`; `Tmds.MDns` was last released in 2023 and has a callback
API; and `DnsClient` has no public entry point that reads a raw message. Taking only the
codec keeps the browse socket strategy in our hands.

Three further limits came out of the review, and each shapes how a library is used:

- **Only the message classes of `Lextm.SharpSnmpLib` are used**, and the response is read
  through `Scope.Pdu`. The `Messenger` helpers own the socket, and the
  `SnmpMessageCompatibilityExtensions` shim reads the protocol data unit by reflection and
  raises trim warning IL2075 in a consuming application, so both are avoided. The datagrams
  travel over our own `IUdpChannel`, which keeps the timeout, the retry count and the table
  walk under our control.
- **`SharpIppNext` does not model the `marker-*` attributes**, which report ink and toner
  levels. `IppMarkers` reads them from the raw attributes of the response, which
  `CapturingIppProtocol` keeps.
- **Only the wire codec of `Makaretu.Dns.New` is used.** It has no send path that fits a
  one-shot browse, so the datagrams travel over `IUdpChannel`, which keeps the ephemeral
  source port that RFC 6762 §6.7 needs. Read a name with `DomainName.Labels` and not with
  `ToString()`: the string form escapes a space as `\032`, which would corrupt the service
  name that de-duplication keys on.

### Watch: SNMP version 13 going stable

Version 13 is still a prerelease and its API is still moving
([issue 703](https://github.com/lextudio/sharpsnmplib/issues/703) covers the trimming
annotations it dropped). Watch for these when 13.x becomes stable:

- Remove the NU5104 suppression from `src/Devices/Devices.csproj`.
- `ResponseMessage`, which the test fixtures use to build a response, is marked
  internal-use-only and may be removed. `SnmpResponses` suppresses CS0618 for it.
- Version 13 adds an `ISnmpTransport` parameter to the send methods. Our own `IUdpChannel`
  could then be dropped for SNMP, if the retry and timeout behaviour is kept.

What stays hand-written is the domain knowledge that no library ships: the **Printer MIB
rules** (the negative supply-level values of RFC 3805, the `hrPrinterDetectedErrorState`
bits, the colorant join) and the **DNS-SD rules** (de-duplication by service name, and the
order that prefers the raw print channel).

## Intra-Repository References

Projects reference each other conditionally by configuration (same convention as the
sibling `common-utilities` repository):

```xml
<ItemGroup Condition="'$(CONFIGURATION)' == 'Debug' Or '$(BuildDocFx)' == 'true'">
  <ProjectReference Include="..\Devices\Devices.csproj" />
</ItemGroup>

<ItemGroup Condition="'$(CONFIGURATION)' == 'Release' And '$(BuildDocFx)' != 'true'">
  <PackageReference Include="AdaptArch.Devices" />
</ItemGroup>
```

- `Debug` builds use live project sources; `Release` builds validate against the real
  published packages (versions pinned in `Directory.Packages.props`).
- The `BuildDocFx` carve-out keeps API documentation building from sources.
- Every new package must be added to the `projects` array in `pipeline/publish-packages.sh`
  (in dependency order) so releases publish it and bump its pinned version.

A future specialized package (`AdaptArch.Devices.Scanners`, for example) follows the
sibling `common-utilities` convention: one package for each independent concern, with as
few dependencies as possible.

## Package Build Configuration

Package-level properties are configured in the root `Directory.Build.props`:

- `PackageId` / `AssemblyName` / `RootNamespace` = `AdaptArch.<ProjectName>`
- License: Apache-2.0
- Symbol package (`.snupkg`) enabled, source link embedded
- `README.md` is included as the package readme; `assets/logo.png` as the package icon

Source projects opt into packaging via `src/Directory.Build.props` (`IsPackable=true`, documentation generation, AOT analyzers).

## Publish Flow

Releases publish via the `pack.yml` workflow, which runs `pipeline/publish-packages.sh`. Packages are pushed to nuget.org and copied into the local `.nuget/` source.

Local package source `.nuget/` alongside nuget.org is configured in `nuget.config` (with package source mapping).
