---
_layout: landing
---

# .NET Devices

A cross-platform .NET library for interacting with hardware devices — printers, scanners, and similar peripherals. Targets Windows, Linux, and macOS.

## Package Overview

### AdaptArch.Devices
**Core cross-platform device package** providing:
- **Device Abstractions**: Common contracts for printers, scanners, and peripherals
- **Cross-Platform Support**: Runs on Windows, Linux, and macOS via `RuntimeInformation`/OS checks
- **Platform Implementations**: OS-specific behavior encapsulated behind shared interfaces

## Key Benefits

✅ **Cross-Platform**: Same API on Windows, Linux, and macOS  
✅ **Testability**: External dependencies abstracted behind interfaces  
✅ **Extensibility**: Add new device types without touching shared code  
✅ **Thread Safety**: Safe concurrent operations  
✅ **Dependency Injection Ready**: Full integration with `Microsoft.Extensions.DependencyInjection`

## Quick Start

```bash
dotnet add package AdaptArch.Devices
```

## Getting Started

1. **Install the package** based on your needs
2. **Register services** in your DI container
3. **Explore the documentation** for detailed usage examples
4. **Check the API reference** for complete method signatures

This library forms the foundation for building robust, cross-platform device-interaction applications in .NET.
