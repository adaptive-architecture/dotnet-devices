---
_layout: landing
---

# .NET Devices

A cross-platform .NET library for interacting with hardware devices — printers, scanners
and similar peripherals. It runs on Windows, Linux and macOS.

## Package overview

### AdaptArch.Devices

**The core package** — the device abstractions and every implementation that needs no extra
engine:

- **Printing**: raw payloads with a content type, IPP and IPPS, raw TCP, the operating system
  spooler, and a CUPS server over the network
- **Discovery**: an mDNS browse that needs no host list, a TCP probe of explicit hosts, and the
  printers installed in the operating system
- **Status**: over IPP, and over SNMP version 2c, which adds the serial number and the page count
- **Jobs**: job queues, job progress, and a monitor that works with every printer
- **Raster**: `PwgRasterWriter`, `PngWriter` and `RasterCanvas`, all public

### AdaptArch.Devices.DependencyInjection

**`Microsoft.Extensions.DependencyInjection` registrations**, kept separate so the core package
holds few dependencies. `services.AddPrinters()` registers everything and hands the container's
`ILoggerFactory` to every part of the library.

### AdaptArch.Devices.Pdfium

**Cross-platform PDF rasterization**, so a PDF prints on a printer that cannot read one. Windows,
Linux and macOS, x64 and ARM alike. It carries the native library.

### AdaptArch.Devices.Windows

**The same job through the in-box Windows engine**, which downloads nothing. Windows 10 and later,
and Windows Server with the Desktop Experience.

## Key benefits

✅ **Cross-platform**: the same API on Windows, Linux and macOS  
✅ **The payload picks the channel**: send ZPL and it reaches a channel that passes the bytes through  
✅ **Testable**: every device interaction sits behind an interface  
✅ **Extensible**: add a format, a converter or a device type without touching the shared code  
✅ **Diagnosable**: structured failures, a graded log, and the raw protocol answer on request  
✅ **Thread-safe**: the registered services are safe to share  
✅ **DI-ready**: `services.AddPrinters()`

## Quick start

```bash
dotnet add package AdaptArch.Devices
dotnet add package AdaptArch.Devices.DependencyInjection
```

```csharp
services.AddPrinters();
```

```csharp
IPrinterManager manager = provider.GetRequiredService<IPrinterManager>();
var devices = await manager.DiscoverAsync(null, cancellationToken).ConfigureAwait(false);

PrinterPayload payload = PrinterPayload.FromString(
    "^XA^FO50,50^ADN,36,20^FDHello^FS^XZ", PrinterContentTypes.Zpl);

await manager.PrintAsync(devices[0].Id, payload, null, cancellationToken).ConfigureAwait(false);
```

## Getting started

1. **[Read the overview](docs/getting-started.md)** — what each package adds, and the first print
2. **[Learn the model](docs/printers.md)** — identifiers, endpoints, channels and devices
3. **[Use the manager](docs/printer-manager.md)** — one entry point that runs every source and
   picks a channel
4. **[When something goes wrong](docs/troubleshooting.md)** — find why a job did not print
5. **Check the API reference** for the complete signatures

[Roadmap](docs/roadmap.md) says what is implemented today and what is planned.
