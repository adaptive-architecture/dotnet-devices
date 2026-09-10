# Overview

.NET Devices is a cross-platform library for interacting with hardware peripherals such as
printers and scanners. It runs on Windows, Linux and macOS.

## Design

The device abstractions live in one shared library. The platform behaviour is picked at run
time with an OS check, or at compile time where that is not possible. Every external device
interaction sits behind an interface, so a unit test can substitute it.

## Install

```bash
dotnet add package AdaptArch.Devices
dotnet add package AdaptArch.Devices.DependencyInjection   # optional
```

```csharp
services.AddPrinters();
```

Then read [Printers](printers.md).
