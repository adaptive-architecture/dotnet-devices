---
_layout: landing
---

# .NET Devices

A cross-platform .NET library for interacting with hardware devices — printers, scanners
and similar peripherals. It runs on Windows, Linux and macOS.

| Package | Contents |
| :--- | :--- |
| `AdaptArch.Devices` | The device abstractions and their implementations. |
| `AdaptArch.Devices.DependencyInjection` | `Microsoft.Extensions.DependencyInjection` registrations, so the core package keeps few dependencies. |

## Key benefits

✅ **Cross-platform**: the same API on Windows, Linux and macOS  
✅ **Testable**: every device interaction sits behind an interface  
✅ **Extensible**: add a device type without touching the shared code  
✅ **Thread-safe**: the registered services are safe to share  
✅ **DI-ready**: `services.AddPrinters()`

## Quick start

```bash
dotnet add package AdaptArch.Devices
```

Read [Printers](docs/printers.md) next, or the API reference for the complete signatures.
