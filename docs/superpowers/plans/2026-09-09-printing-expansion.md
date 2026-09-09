# Printing Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `AdaptArch.Devices` a spooler transport, a `PrinterConfiguration` producer, print job submission over IPP, and job progress monitoring, and make `IPrinter` real.

**Architecture:** One core package with runtime operating system checks. The IPP code becomes the shared engine: network printers speak IPP directly, and the CUPS spooler on Linux and macOS is an IPP server on `localhost:631`. Windows is the only platform that needs native interop, through `LibraryImport` bindings to `winspool.drv`.

**Tech Stack:** .NET 10, C#, xunit.v3 with the Microsoft Testing Platform runner, NSubstitute, `SharpIppNext` 4.2.4, `Lextm.SharpSnmpLib` 13.0.0-beta.3, `Makaretu.Dns.New` 3.1.2.

**Spec:** `docs/superpowers/specs/2026-09-09-printing-expansion-design.md`

## Global Constraints

- **All `dotnet` operations go through `dotnetup`**: `dotnetup dotnet build`, `dotnetup dotnet test`, `dotnetup dotnet format`. Never call the system `dotnet`.
- **RCS1090 is an error**: every `await` must call `.ConfigureAwait(false)`.
- **`var` for all locals** (`IDE0007`, error). **Language keywords for all declarations** — fields, properties, parameters, return types, generic arguments, casts: `string`, `int`, `bool` (`IDE0049`, error). Use the BCL name only for static access (`String.Empty`, `String.Equals`, `Int32.MaxValue`).
- **Code style**: 4-space indent, UTF-8 BOM on every file, expression-bodied members preferred, no `this.`, `System.*` usings first, no primary constructors (IDE0290), no switch expressions (IDE0066).
- **`src/` sets `IsAotCompatible` and treats warnings as errors.** All new code must be trim-safe and native-AOT-safe. Use `LibraryImport`, never `DllImport`.
- **No new NuGet dependency.** Every dependency needs sign-off; this plan needs none.
- **Namespace**: `AdaptArch.Devices.Printing` for the printing types, `AdaptArch.Devices.Printing.Discovery` for the discovery and status types. Test namespace mirrors it under `AdaptArch.Devices.UnitTests.Printing`.
- **Public types need XML documentation.** The build treats a missing comment as an error.
- **Run a single test class**: from `test/Devices.UnitTests`, `dotnetup dotnet test --filter "FullyQualifiedName~ClassName"`.
- **Run the full suite**: from the repository root, `dotnetup dotnet test`.
- **Commit after every task.** Do not batch commits.

## File Structure

**Create in `src/Devices/Printing/Ipp/`:**

| File | Responsibility |
| --- | --- |
| `IppEndpointResolver.cs` | Finds the working printer URI one time and keeps it. |
| `IppOperations.cs` | Builds a `SharpIppClient` for a URI and maps its exceptions. |
| `IppConfigurationMapper.cs` | `PrinterAttributes` to `PrinterConfiguration`. |
| `IppJobMapper.cs` | `JobDescriptionAttributes` to `PrintJobInfo`, and the job state map. |
| `IppStatusMapper.cs` | `PrinterDescriptionAttributes` to `PrinterInfo` and `PrinterStatus`. |
| `IppJobTemplateMapper.cs` | `PrintOptions` to `JobTemplateAttributes`. |
| `IppPrinter.cs` | `IPrinter` over IPP. |
| `IppPrintJobQueue.cs` | `IPrintJobQueue` over IPP. |

**Create in `src/Devices/Printing/`:**

| File | Responsibility |
| --- | --- |
| `UnsupportedOptionBehavior.cs` | The enum. |
| `PrintOptionValidator.cs` | Checks options against a configuration; throws or drops. |
| `RawPrinter.cs` | `IPrinter` over `TcpPrinterTransport`. |
| `IPrinterFactory.cs`, `PrinterFactory.cs` | Endpoint to `IPrinter`. |
| `IPrintJobMonitor.cs`, `PrintJobMonitorOptions.cs`, `PollingPrintJobMonitor.cs` | Progress monitoring. |
| `CompositePrintJobQueue.cs` | Sends a request to the IPP queue or the spooler queue. It takes `SpoolerPrintJobQueue` as a concrete type, never `IPrintJobQueue`, because it is itself the registered `IPrintJobQueue` and would otherwise depend on itself. |

**Create in `src/Devices/Printing/Spooler/`:**

| File | Responsibility |
| --- | --- |
| `ISpoolerDriver.cs` | The internal platform seam. |
| `SpoolerDriverFactory.cs` | Selects the driver for the operating system. |
| `CupsSpoolerDriver.cs` | IPP to the local CUPS daemon. |
| `WindowsSpoolerDriver.cs` | Job and queue logic over the interop layer. |
| `WindowsSpoolerInterop.cs` | `LibraryImport` declarations and structures only. |
| `SpoolerPrinter.cs` | `IPrinter` over `ISpoolerDriver`. |
| `SpoolerPrinterDiscovery.cs` | `IPrinterDiscovery` over `ISpoolerDriver`. |
| `SpoolerPrintJobQueue.cs` | `IPrintJobQueue` over `ISpoolerDriver`. |

**Modify:**

- `src/Devices/Printing/PrintOptions.cs` — add `OnUnsupported`.
- `src/Devices/Printing/PrintJobInfo.cs` — add four properties.
- `src/Devices/Printing/IppPrinterStatusClient.cs` — call the new internal types.
- `src/Devices.DependencyInjection/ServiceCollectionExtensions.cs` — new registrations.
- `samples/Devices.Samples/Program.cs` — the `--watch` command.
- `docs/printers.md`, `docs/capabilities.md` — the documentation update.

---

## Phase 1 — IPP core and configuration

### Task 1: Share the IPP test helpers

`IppPrinterStatusClientTests` holds `BuildResponse`, `IppOk` and `StubHandler` as private members. Every new IPP test needs them. Move them to a shared class first, so no later task copies code.

**Files:**
- Create: `test/Devices.UnitTests/Printing/Ipp/IppMessages.cs`
- Modify: `test/Devices.UnitTests/Printing/IppPrinterStatusClientTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class IppMessages` with `public static byte[] Response(ushort status, params (byte Tag, string Name, object Value)[] attributes)`, `public static HttpResponseMessage Ok(byte[] payload)`, and `internal sealed class StubHandler : HttpMessageHandler` with a `List<HttpRequestMessage> Requests` property and a constructor that takes `Func<HttpRequestMessage, HttpResponseMessage>`.

- [ ] **Step 1: Create the shared helper**

Create `test/Devices.UnitTests/Printing/Ipp/IppMessages.cs`. Move the bodies of `BuildResponse`, `IppOk` and `StubHandler` from `IppPrinterStatusClientTests.cs` without changing them. Rename `BuildResponse` to `Response` and `IppOk` to `Ok`. Make the class and its members `internal` or `public static` as the interface block above states.

```csharp
using System.Net;
using System.Text;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Builds IPP response bytes by hand, so a test states exactly which attributes the
// printer sends back. The typed model of SharpIppNext cannot express a malformed or
// partial answer, which several tests need.
internal static class IppMessages
{
    public static HttpResponseMessage Ok(byte[] payload) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    public static byte[] Response(ushort status, params (byte Tag, string Name, object Value)[] attributes)
    {
        // Move the existing body of BuildResponse here without any change.
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        // Move the existing StubHandler here without any change.
    }
}
```

- [ ] **Step 2: Point the existing tests at the helper**

In `IppPrinterStatusClientTests.cs`, delete the three private members. Add `using AdaptArch.Devices.UnitTests.Printing.Ipp;`. Replace every `BuildResponse(` with `IppMessages.Response(`, every `IppOk(` with `IppMessages.Ok(`, and every `StubHandler` with `IppMessages.StubHandler`.

- [ ] **Step 3: Run the tests, which must still pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppPrinterStatusClientTests"`
Expected: PASS. This step is a refactor. A failure means the move changed behaviour.

- [ ] **Step 4: Commit**

```bash
git add test/Devices.UnitTests/Printing/Ipp/IppMessages.cs test/Devices.UnitTests/Printing/IppPrinterStatusClientTests.cs
git commit -m "test: share the IPP message helpers"
```

---

### Task 2: IppEndpointResolver

The status client probes `ipps` then `ipp`, across `/ipp/print` and `/ipp/port1`, inside one method. Printing, the configuration read and the job queries need the same probe. Extract it, and make it keep the answer.

**Files:**
- Create: `src/Devices/Printing/Ipp/IppEndpointResolver.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppEndpointResolverTests.cs`

**Interfaces:**
- Consumes: `IppMessages` from Task 1.
- Produces: `internal sealed class IppEndpointResolver` with the constructor `IppEndpointResolver(HttpClient httpClient, string host, int port, string? resourcePath)` and the method `Task<Uri> ResolveAsync(CancellationToken cancellationToken)`. It throws `InvalidOperationException` when no candidate answers. A second call returns the kept URI and sends no request.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppEndpointResolverTests.cs`:

```csharp
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppEndpointResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsTheFirstUriThatAnswers()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri.Scheme == "https"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ipp", uri.Scheme);
        Assert.Equal("/ipp/print", uri.AbsolutePath);
    }

    [Fact]
    public async Task ResolveAsync_KeepsTheAnswerAndSendsNoSecondRequest()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var first = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
        var second = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResolveAsync_TriesTheCallerPathFirst()
    {
        var body = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, "printers/lobby");

        var uri = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/printers/lobby", uri.AbsolutePath);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsWhenNothingAnswers()
    {
        IppMessages.StubHandler handler = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        IppEndpointResolver resolver = new(new HttpClient(handler), "printer.local", 631, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        Assert.Contains("printer.local:631", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppEndpointResolverTests"`
Expected: the build fails, because `IppEndpointResolver` does not exist.

- [ ] **Step 3: Write the resolver**

Create `src/Devices/Printing/Ipp/IppEndpointResolver.cs`. Move the `Schemes`, `ResourcePaths` and `GetResourcePaths` members out of `IppPrinterStatusClient` into this class. The probe sends a `GetPrinterAttributesRequest` that asks only for `printer-state`, which every IPP printer answers. A candidate that gives no answer is skipped; the loop moves on.

```csharp
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Finds the printer URI one time. A printer serves IPP at a path that differs between
// makers, so the probe tries the caller path first, then the two well-known paths, over
// TLS and then plain. The answer is kept, because every later operation needs the same URI.
internal sealed class IppEndpointResolver
{
    private static readonly string[] Schemes = ["ipps", "ipp"];
    private static readonly string[] WellKnownPaths = ["/ipp/print", "/ipp/port1"];
    private static readonly string[] ProbeAttributes = ["printer-state"];

    private readonly HttpClient _httpClient;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _resourcePath;
    private Uri? _resolved;

    public IppEndpointResolver(HttpClient httpClient, string host, int port, string? resourcePath)
    {
        _httpClient = httpClient;
        _host = host;
        _port = port;
        _resourcePath = resourcePath;
    }

    public async Task<Uri> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        foreach (var scheme in Schemes)
        {
            foreach (var path in GetPaths(_resourcePath))
            {
                Uri uri = new($"{scheme}://{_host}:{_port}{path}");
                if (await AnswersAsync(uri, cancellationToken).ConfigureAwait(false))
                {
                    _resolved = uri;
                    return uri;
                }
            }
        }

        throw new InvalidOperationException($"Printer '{_host}:{_port}' did not answer IPP over IPPS or IPP.");
    }

    private async Task<bool> AnswersAsync(Uri uri, CancellationToken cancellationToken)
    {
        using SharpIppClient client = new(_httpClient, new IppProtocol());
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = ProbeAttributes },
        };

        try
        {
            _ = await client.GetPrinterAttributesAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException)
        {
            return false;
        }
    }

    // A path from a DNS-SD "rp" attribute has no leading slash, and it may repeat one of
    // the well-known paths, so it is normalized and then de-duplicated.
    private static IReadOnlyList<string> GetPaths(string? resourcePath)
    {
        // Move the existing body of IppPrinterStatusClient.GetResourcePaths here.
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppEndpointResolverTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/Ipp/IppEndpointResolver.cs test/Devices.UnitTests/Printing/Ipp/IppEndpointResolverTests.cs
git commit -m "feat: add the IPP endpoint resolver"
```

---

### Task 3: IppOperations, and the status client refactor

**Files:**
- Create: `src/Devices/Printing/Ipp/IppOperations.cs`
- Modify: `src/Devices/Printing/IppPrinterStatusClient.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppOperationsTests.cs`

**Interfaces:**
- Consumes: `IppEndpointResolver` from Task 2.
- Produces: `internal sealed class IppOperations` with the constructor `IppOperations(HttpClient httpClient)` and the method `Task<TResponse> SendAsync<TRequest, TResponse>(Func<SharpIppClient, TRequest, CancellationToken, Task<TResponse>> operation, TRequest request, Uri uri, CancellationToken cancellationToken)`. It also exposes `IIppResponseMessage? LastRawResponse { get; }`, because the marker attributes are read from the raw response.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppOperationsTests.cs`:

```csharp
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppOperationsTests
{
    private static readonly Uri Printer = new("ipp://printer.local:631/ipp/print");

    [Fact]
    public async Task SendAsync_MapsAnIppErrorToInvalidOperationException()
    {
        // 0x0400 is client-error-bad-request.
        var body = IppMessages.Response(0x0400);
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));

        Assert.Contains("printer.local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MapsAMalformedAnswerToInvalidDataException()
    {
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok([0x09, 0x09, 0x00]))));

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_KeepsTheRawResponseForMarkerReads()
    {
        var body = IppMessages.Response(0x0000, (0x42, "marker-names", "Black ink"));
        IppOperations operations = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        _ = await operations.SendAsync(
            (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            NewRequest(),
            Printer,
            TestContext.Current.CancellationToken);

        Assert.NotNull(operations.LastRawResponse);
    }

    private static GetPrinterAttributesRequest NewRequest() =>
        new() { OperationAttributes = new() { PrinterUri = Printer } };
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppOperationsTests"`
Expected: the build fails, because `IppOperations` does not exist.

- [ ] **Step 3: Write IppOperations**

Create `src/Devices/Printing/Ipp/IppOperations.cs`. Move the `catch` blocks of `IppPrinterStatusClient.TryGetAttributesAsync` here, but let a cancelled or refused request throw rather than return `null`, because the resolver already handled the probe.

```csharp
using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

// Sends one IPP operation and gives every failure the same shape. A fresh protocol and a
// fresh client belong to each call, so the captured raw response belongs to that call only.
internal sealed class IppOperations
{
    private readonly HttpClient _httpClient;

    public IppOperations(HttpClient httpClient) => _httpClient = httpClient;

    public IIppResponseMessage? LastRawResponse { get; private set; }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        Func<SharpIppClient, TRequest, CancellationToken, Task<TResponse>> operation,
        TRequest request,
        Uri uri,
        CancellationToken cancellationToken)
    {
        CapturingIppProtocol capture = new(new IppProtocol());
        using SharpIppClient client = new(_httpClient, capture);
        try
        {
            var response = await operation(client, request, cancellationToken).ConfigureAwait(false);
            LastRawResponse = capture.Response;
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"IPP request to '{uri}' failed with HTTP {exception.StatusCode:d}.", exception);
        }
        catch (IppResponseException exception)
        {
            throw new InvalidOperationException($"Printer '{uri}' reported an IPP error.", exception);
        }
        catch (IppRequestException exception)
        {
            throw new InvalidDataException($"The IPP response from '{uri}' is malformed.", exception);
        }
    }
}
```

Move `CapturingIppProtocol.cs` from `src/Devices/Printing/` to `src/Devices/Printing/Ipp/` and change its namespace to `AdaptArch.Devices.Printing.Ipp`.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppOperationsTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Refactor the status client onto the two new types**

In `IppPrinterStatusClient.GetDetailsAsync`, replace the two nested loops with a call to `IppEndpointResolver.ResolveAsync` and then one call to `IppOperations.SendAsync`. Delete `Schemes`, `ResourcePaths`, `GetResourcePaths` and `TryGetAttributesAsync`. Read the markers from `operations.LastRawResponse`. Keep the public signature and the two constructors exactly as they are.

```csharp
public async Task<IppPrinterDetails> GetDetailsAsync(string host, CancellationToken cancellationToken, int port = DefaultPort, string? resourcePath = null)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(host);
    ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

    IppEndpointResolver resolver = new(_httpClient, host, port, resourcePath);
    var uri = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
    IppOperations operations = new(_httpClient);
    GetPrinterAttributesRequest request = new()
    {
        OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = RequestedAttributes },
    };
    var response = await operations.SendAsync(
        (client, message, token) => client.GetPrinterAttributesAsync(message, token),
        request,
        uri,
        cancellationToken).ConfigureAwait(false);

    return CreateDetails(host, response, operations.LastRawResponse);
}
```

- [ ] **Step 6: Run the whole suite, which must still pass**

Run from the repository root: `dotnetup dotnet test`
Expected: PASS. The existing `IppPrinterStatusClientTests` prove the refactor kept the behaviour. If a test that expected one request now sees two, that is correct: the resolver probes and then the read follows. Update the count assertion in that test only, and add a comment that says why.

- [ ] **Step 7: Commit**

```bash
git add src/Devices/Printing/Ipp/ src/Devices/Printing/IppPrinterStatusClient.cs test/Devices.UnitTests/Printing/
git commit -m "refactor: put the IPP probe and the error map behind shared types"
```

---

### Task 4: The IPP configuration mapper

**Files:**
- Create: `src/Devices/Printing/Ipp/IppConfigurationMapper.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppConfigurationMapperTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `internal static class IppConfigurationMapper` with `public static PrinterConfiguration Map(PrinterId id, PrinterDescriptionAttributes? attributes)`. `SharpIpp.Protocol.Models.PrinterDescriptionAttributes` is the type of `GetPrinterAttributesResponse.PrinterAttributes`; this is confirmed against version 4.2.4.
- Produces: the constant list `public static readonly string[] RequestedAttributes` holding `printer-resolution-supported`, `sides-supported`, `color-supported`, `media-supported` and `media-default`.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppConfigurationMapperTests.cs`. Build a real response with `IppMessages.Response`, send it through `IppPrinterStatusClient`'s sibling path, and then map it. The simplest correct test builds the attributes object with `SharpIppClient` by decoding bytes, so the test proves the mapping against a real decode.

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppConfigurationMapperTests
{
    private static readonly PrinterId Id = PrinterId.FromNetwork("printer.local");

    [Fact]
    public async Task Map_ReadsResolutionsSidesColourAndMedia()
    {
        // 0x32 is the resolution tag; the value is width, height and the unit 3 (dpi).
        var body = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x44, null, "two-sided-long-edge"),
            (0x22, "color-supported", (byte)1),
            (0x44, "media-supported", "iso_a4_210x297mm"),
            (0x44, null, "na_letter_8.5x11in"),
            (0x44, "media-default", "iso_a4_210x297mm"));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes);

        Assert.Equal(Id, configuration.PrinterId);
        Assert.True(configuration.SupportsDuplex);
        Assert.True(configuration.SupportsColor);
        Assert.Equal(["iso_a4_210x297mm", "na_letter_8.5x11in"], configuration.MediaSizes);
        Assert.Equal("iso_a4_210x297mm", configuration.DefaultMediaSize);
    }

    [Fact]
    public async Task Map_ReportsNoDuplexWhenOnlyOneSidedIsSupported()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes);

        Assert.False(configuration.SupportsDuplex);
        Assert.False(configuration.SupportsColor);
    }

    [Fact]
    public void Map_ReturnsAnEmptyConfigurationForNoAttributes()
    {
        var configuration = IppConfigurationMapper.Map(Id, null);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.False(configuration.SupportsDuplex);
        Assert.Null(configuration.DefaultMediaSize);
    }
}
```

- [ ] **Step 2: Add the decode helper to IppMessages**

Add this method to `IppMessages` from Task 1. It sends the bytes through a stub handler and returns the typed attributes, so a mapping test needs no hand-built model object.

```csharp
public static async Task<PrinterDescriptionAttributes> DecodePrinterAttributesAsync(byte[] body)
{
    Uri uri = new("ipp://printer.local:631/ipp/print");
    using SharpIppClient client = new(new HttpClient(new StubHandler(_ => Ok(body))), new IppProtocol());
    var response = await client.GetPrinterAttributesAsync(
        new GetPrinterAttributesRequest { OperationAttributes = new() { PrinterUri = uri } },
        CancellationToken.None).ConfigureAwait(false);
    return response.PrinterAttributes;
}
```

Add the matching usings: `SharpIpp`, `SharpIpp.Models.Requests`, `SharpIpp.Protocol`, `SharpIpp.Protocol.Models`.

- [ ] **Step 3: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppConfigurationMapperTests"`
Expected: the build fails, because `IppConfigurationMapper` does not exist.

- [ ] **Step 4: Write the mapper**

Create `src/Devices/Printing/Ipp/IppConfigurationMapper.cs`. `Sides` and `Media` are value types with a `Value` string. `PrinterResolutionSupported` is a `Resolution[]`, where `Units` says `DotsPerInch` or `DotsPerCm`; keep only the `DotsPerInch` entries and use `Width`.

```csharp
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns the IPP printer attributes into the library model. An absent attribute means the
// printer did not report the capability, which the model shows as an empty list or false.
internal static class IppConfigurationMapper
{
    public static readonly string[] RequestedAttributes =
    [
        "printer-resolution-supported",
        "sides-supported",
        "color-supported",
        "media-supported",
        "media-default",
    ];

    public static PrinterConfiguration Map(PrinterId id, PrinterDescriptionAttributes? attributes)
    {
        PrinterConfiguration configuration = new(id);
        if (attributes is null)
        {
            return configuration;
        }

        configuration.SupportsColor = attributes.ColorSupported ?? false;
        configuration.SupportsDuplex = HasDuplex(attributes.SidesSupported);
        configuration.MediaSizes = ReadMedia(attributes.MediaSupported);
        configuration.DefaultMediaSize = attributes.MediaDefault?.Value;
        configuration.SupportedResolutionsDpi = ReadResolutions(attributes.PrinterResolutionSupported);
        return configuration;
    }

    // A printer that reports only "one-sided" cannot print on two sides.
    private static bool HasDuplex(Sides[]? sides)
    {
        if (sides is null)
        {
            return false;
        }

        foreach (var side in sides)
        {
            if (side.Value is not null && side.Value.StartsWith("two-sided", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ReadMedia(Media[]? media)
    {
        if (media is null || media.Length == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (var entry in media)
        {
            if (!String.IsNullOrWhiteSpace(entry.Value))
            {
                names.Add(entry.Value);
            }
        }

        return names;
    }

    // The Printer MIB and IPP both allow dots per centimetre. The model holds dots per
    // inch only, so an entry in another unit is not reported.
    private static IReadOnlyList<int> ReadResolutions(Resolution[]? resolutions)
    {
        if (resolutions is null || resolutions.Length == 0)
        {
            return [];
        }

        List<int> dpi = [];
        foreach (var resolution in resolutions)
        {
            if (resolution.Units == ResolutionUnit.DotsPerInch && !dpi.Contains(resolution.Width))
            {
                dpi.Add(resolution.Width);
            }
        }

        return dpi;
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppConfigurationMapperTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Devices/Printing/Ipp/IppConfigurationMapper.cs test/Devices.UnitTests/Printing/Ipp/
git commit -m "feat: map the IPP printer attributes to PrinterConfiguration"
```

---

## Phase 2 — IPP printing

### Task 5: The model changes

**Files:**
- Create: `src/Devices/Printing/UnsupportedOptionBehavior.cs`
- Modify: `src/Devices/Printing/PrintOptions.cs`, `src/Devices/Printing/PrintJobInfo.cs`
- Test: `test/Devices.UnitTests/Printing/PrintingModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public enum UnsupportedOptionBehavior { Send, Throw, Drop }`; `PrintOptions.OnUnsupported` (default `Send`); `PrintJobInfo.ImpressionsCompleted` (`int?`), `PrintJobInfo.TotalImpressions` (`int?`), `PrintJobInfo.Detail` (`string?`), `PrintJobInfo.DroppedOptions` (`IReadOnlyList<string>`, default empty).

- [ ] **Step 1: Write the failing tests**

Add to `test/Devices.UnitTests/Printing/PrintingModelTests.cs`:

```csharp
[Fact]
public void PrintOptions_DefaultsToSendingUnsupportedOptions()
{
    PrintOptions options = new();

    Assert.Equal(UnsupportedOptionBehavior.Send, options.OnUnsupported);
}

[Fact]
public void PrintJobInfo_HasNoProgressAndNoDroppedOptionsByDefault()
{
    PrintJobInfo job = new("42", PrinterId.FromNetwork("printer.local"), PrintJobState.Queued);

    Assert.Null(job.ImpressionsCompleted);
    Assert.Null(job.TotalImpressions);
    Assert.Null(job.Detail);
    Assert.Empty(job.DroppedOptions);
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrintingModelTests"`
Expected: the build fails on `UnsupportedOptionBehavior` and the four new properties.

- [ ] **Step 3: Write the enum and the properties**

Create `src/Devices/Printing/UnsupportedOptionBehavior.cs`:

```csharp
namespace AdaptArch.Devices.Printing;

/// <summary>
/// What <see cref="IPrinter.PrintAsync"/> does with an option the printer does not support.
/// </summary>
public enum UnsupportedOptionBehavior
{
    /// <summary>
    /// Send the option and let the printer decide. This costs no extra request.
    /// </summary>
    Send,

    /// <summary>
    /// Read the configuration first, then throw <see cref="NotSupportedException"/>.
    /// </summary>
    Throw,

    /// <summary>
    /// Read the configuration first, remove the option, and name it in
    /// <see cref="PrintJobInfo.DroppedOptions"/>.
    /// </summary>
    Drop,
}
```

Add to `PrintOptions`:

```csharp
/// <summary>
/// Gets or sets what to do with an option the printer does not support.
/// A printer that reports no configuration cannot say which options it supports,
/// so <see cref="UnsupportedOptionBehavior.Throw"/> and
/// <see cref="UnsupportedOptionBehavior.Drop"/> then act as
/// <see cref="UnsupportedOptionBehavior.Send"/>.
/// </summary>
public UnsupportedOptionBehavior OnUnsupported { get; set; } = UnsupportedOptionBehavior.Send;
```

Add to `PrintJobInfo`:

```csharp
/// <summary>
/// Gets or sets the number of pages printed, when the source reports it.
/// </summary>
public int? ImpressionsCompleted { get; set; }

/// <summary>
/// Gets or sets the total number of pages in the job, when the source reports it.
/// </summary>
public int? TotalImpressions { get; set; }

/// <summary>
/// Gets or sets a readable reason for the current state, when the source reports one.
/// </summary>
public string? Detail { get; set; }

/// <summary>
/// Gets or sets the options that were removed because the printer does not support them.
/// This is empty unless <see cref="UnsupportedOptionBehavior.Drop"/> was used.
/// </summary>
public IReadOnlyList<string> DroppedOptions { get; set; } = [];
```

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrintingModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/UnsupportedOptionBehavior.cs src/Devices/Printing/PrintOptions.cs src/Devices/Printing/PrintJobInfo.cs test/Devices.UnitTests/Printing/PrintingModelTests.cs
git commit -m "feat: add the unsupported-option behaviour and the job progress fields"
```

---

### Task 6: The option to job-template mapper

**Files:**
- Create: `src/Devices/Printing/Ipp/IppJobTemplateMapper.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppJobTemplateMapperTests.cs`

**Interfaces:**
- Consumes: `PrintOptions` from Task 5.
- Produces: `internal static class IppJobTemplateMapper` with `public static JobTemplateAttributes Map(PrintOptions? options)`.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppJobTemplateMapperTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol.Models;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppJobTemplateMapperTests
{
    [Fact]
    public void Map_TranslatesEveryOption()
    {
        PrintOptions options = new()
        {
            Copies = 3,
            Duplex = DuplexMode.TwoSidedLongEdge,
            ColorMode = PrintColorMode.Monochrome,
            Orientation = PrintOrientation.Landscape,
            MediaSize = "iso_a4_210x297mm",
            MediaSource = "tray-1",
            ResolutionDpi = 600,
        };

        var template = IppJobTemplateMapper.Map(options);

        Assert.Equal(3, template.Copies);
        Assert.Equal("two-sided-long-edge", template.Sides?.Value);
        Assert.Equal("monochrome", template.PrintColorMode?.Value);
        Assert.Equal(Orientation.Landscape, template.OrientationRequested);
        Assert.Equal("iso_a4_210x297mm", template.Media?.Value);
        Assert.Equal(600, template.PrinterResolution?.Width);
        Assert.Equal(ResolutionUnit.DotsPerInch, template.PrinterResolution?.Units);
    }

    [Fact]
    public void Map_LeavesEveryUnsetOptionUnset()
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions());

        Assert.Null(template.Copies);
        Assert.Null(template.Sides);
        Assert.Null(template.PrintColorMode);
        Assert.Null(template.OrientationRequested);
        Assert.Null(template.Media);
        Assert.Null(template.PrinterResolution);
    }

    [Fact]
    public void Map_ReturnsAnEmptyTemplateForNoOptions()
    {
        var template = IppJobTemplateMapper.Map(null);

        Assert.Null(template.Copies);
    }
}
```

Read `DuplexMode`, `PrintColorMode` and `PrintOrientation` in `src/Devices/Printing/` before writing the mapper, and use the member names those files actually declare. If a member named in the test above does not exist, use the real name in both the test and the mapper.

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppJobTemplateMapperTests"`
Expected: the build fails, because `IppJobTemplateMapper` does not exist.

- [ ] **Step 3: Write the mapper**

Create `src/Devices/Printing/Ipp/IppJobTemplateMapper.cs`. `Sides`, `Media` and `PrintColorMode` in `SharpIpp.Protocol.Models` are value types with well-known static members (`Sides.OneSided`, `Sides.TwoSidedLongEdge`, `Sides.TwoSidedShortEdge`, `PrintColorMode.Color`, `PrintColorMode.Monochrome`, `PrintColorMode.Auto`). `Media` also has a constructor that takes a name string, which the mapper needs, because a media size name is free text.

```csharp
using SharpIpp.Protocol.Models;
using IppColorMode = SharpIpp.Protocol.Models.PrintColorMode;
using IppOrientation = SharpIpp.Protocol.Models.Orientation;

namespace AdaptArch.Devices.Printing.Ipp;

// Turns the library options into IPP job template attributes. An option the caller did not
// set stays unset, so the printer applies its own default.
internal static class IppJobTemplateMapper
{
    public static JobTemplateAttributes Map(PrintOptions? options)
    {
        JobTemplateAttributes template = new();
        if (options is null)
        {
            return template;
        }

        template.Copies = options.Copies;
        template.Sides = MapSides(options.Duplex);
        template.PrintColorMode = MapColor(options.ColorMode);
        template.OrientationRequested = MapOrientation(options.Orientation);
        if (!String.IsNullOrWhiteSpace(options.MediaSize))
        {
            template.Media = new Media(options.MediaSize);
        }

        if (!String.IsNullOrWhiteSpace(options.MediaSource))
        {
            template.MediaSource = new MediaSource(options.MediaSource);
        }

        if (options.ResolutionDpi is int dpi)
        {
            template.PrinterResolution = new Resolution(dpi, dpi, ResolutionUnit.DotsPerInch);
        }

        return template;
    }

    // IDE0066 turns off switch expressions in this repository, so each map is a chain
    // of if statements.
    private static Sides? MapSides(DuplexMode? duplex)
    {
        if (duplex == DuplexMode.OneSided)
        {
            return Sides.OneSided;
        }

        if (duplex == DuplexMode.TwoSidedLongEdge)
        {
            return Sides.TwoSidedLongEdge;
        }

        if (duplex == DuplexMode.TwoSidedShortEdge)
        {
            return Sides.TwoSidedShortEdge;
        }

        return null;
    }

    private static IppColorMode? MapColor(PrintColorMode? mode)
    {
        if (mode == PrintColorMode.Color)
        {
            return IppColorMode.Color;
        }

        if (mode == PrintColorMode.Monochrome)
        {
            return IppColorMode.Monochrome;
        }

        return null;
    }

    private static IppOrientation? MapOrientation(PrintOrientation? orientation)
    {
        if (orientation == PrintOrientation.Portrait)
        {
            return IppOrientation.Portrait;
        }

        if (orientation == PrintOrientation.Landscape)
        {
            return IppOrientation.Landscape;
        }

        return null;
    }
}
```

The member names above are the ones `Sides`, `IppColorMode` and `IppOrientation` really declare in version 4.2.4. `DuplexMode`, `PrintColorMode` and `PrintOrientation` are this library's own enums: open those three files and use the member names they declare. If a name differs from the one written here, change both this mapper and the test.

`Media`, `MediaSource` and `Resolution` are value types whose constructors carry extra flags:

- `Media(string value, bool isValue, bool isMarked)` — use `new Media(options.MediaSize, true, false)`.
- `MediaSource(string value, bool isValue)` — use `new MediaSource(options.MediaSource, true)`.
- `Resolution(int width, int height, ResolutionUnit units, bool isValue)` — use `new Resolution(dpi, dpi, ResolutionUnit.DotsPerInch, true)`.

The tests above assert `template.Media?.Value` and `template.PrinterResolution?.Width`, so a wrong flag shows up at once as a failing assertion.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppJobTemplateMapperTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/Ipp/IppJobTemplateMapper.cs test/Devices.UnitTests/Printing/Ipp/IppJobTemplateMapperTests.cs
git commit -m "feat: map PrintOptions to the IPP job template"
```

---

### Task 7: The option validator

**Files:**
- Create: `src/Devices/Printing/PrintOptionValidator.cs`
- Test: `test/Devices.UnitTests/Printing/PrintOptionValidatorTests.cs`

**Interfaces:**
- Consumes: `UnsupportedOptionBehavior`, `PrintOptions`, `PrinterConfiguration`.
- Produces: `internal static class PrintOptionValidator` with `public static PrintOptions? Apply(PrintOptions? options, PrinterConfiguration configuration, out IReadOnlyList<string> dropped)`. With `Send`, or with an empty configuration, it returns the options unchanged and an empty `dropped`. With `Throw` it throws `NotSupportedException`. With `Drop` it returns a copy that has the unsupported options cleared, and names them in `dropped`.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/PrintOptionValidatorTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintOptionValidatorTests
{
    private static PrinterConfiguration SimplexA4() =>
        new(PrinterId.FromNetwork("printer.local"))
        {
            SupportsDuplex = false,
            SupportsColor = false,
            MediaSizes = ["iso_a4_210x297mm"],
            SupportedResolutionsDpi = [300],
        };

    [Fact]
    public void Apply_SendPassesEveryOptionThrough()
    {
        PrintOptions options = new() { Duplex = DuplexMode.TwoSidedLongEdge, OnUnsupported = UnsupportedOptionBehavior.Send };

        var result = PrintOptionValidator.Apply(options, SimplexA4(), out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_ThrowNamesTheUnsupportedOption()
    {
        PrintOptions options = new() { Duplex = DuplexMode.TwoSidedLongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw };

        var error = Assert.Throws<NotSupportedException>(
            () => PrintOptionValidator.Apply(options, SimplexA4(), out _));

        Assert.Contains("Duplex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DropRemovesTheOptionAndNamesIt()
    {
        PrintOptions options = new()
        {
            Copies = 2,
            Duplex = DuplexMode.TwoSidedLongEdge,
            MediaSize = "na_letter_8.5x11in",
            OnUnsupported = UnsupportedOptionBehavior.Drop,
        };

        var result = PrintOptionValidator.Apply(options, SimplexA4(), out var dropped);

        Assert.Null(result.Duplex);
        Assert.Null(result.MediaSize);
        Assert.Equal(2, result.Copies);
        Assert.Equal(["Duplex", "MediaSize"], dropped);
    }

    [Fact]
    public void Apply_AnEmptyConfigurationCannotJudgeAnything()
    {
        PrintOptions options = new() { Duplex = DuplexMode.TwoSidedLongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw };
        PrinterConfiguration empty = new(PrinterId.FromNetwork("printer.local"));

        var result = PrintOptionValidator.Apply(options, empty, out var dropped);

        Assert.Same(options, result);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Apply_ReturnsNullForNoOptions()
    {
        var result = PrintOptionValidator.Apply(null, SimplexA4(), out var dropped);

        Assert.Null(result);
        Assert.Empty(dropped);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrintOptionValidatorTests"`
Expected: the build fails, because `PrintOptionValidator` does not exist.

- [ ] **Step 3: Write the validator**

Create `src/Devices/Printing/PrintOptionValidator.cs`:

```csharp
namespace AdaptArch.Devices.Printing;

// Compares the options against what the printer says it can do. A printer that reported
// nothing cannot judge anything, so the options pass unchanged.
internal static class PrintOptionValidator
{
    public static PrintOptions? Apply(PrintOptions? options, PrinterConfiguration configuration, out IReadOnlyList<string> dropped)
    {
        dropped = [];
        if (options is null || options.OnUnsupported == UnsupportedOptionBehavior.Send || IsEmpty(configuration))
        {
            return options;
        }

        List<string> unsupported = [];
        if (options.Duplex is not null && options.Duplex != DuplexMode.OneSided && !configuration.SupportsDuplex)
        {
            unsupported.Add(nameof(PrintOptions.Duplex));
        }

        if (options.ColorMode == PrintColorMode.Color && !configuration.SupportsColor)
        {
            unsupported.Add(nameof(PrintOptions.ColorMode));
        }

        if (options.MediaSize is not null
            && configuration.MediaSizes.Count > 0
            && !configuration.MediaSizes.Contains(options.MediaSize, StringComparer.Ordinal))
        {
            unsupported.Add(nameof(PrintOptions.MediaSize));
        }

        if (options.ResolutionDpi is int dpi
            && configuration.SupportedResolutionsDpi.Count > 0
            && !configuration.SupportedResolutionsDpi.Contains(dpi))
        {
            unsupported.Add(nameof(PrintOptions.ResolutionDpi));
        }

        if (unsupported.Count == 0)
        {
            return options;
        }

        if (options.OnUnsupported == UnsupportedOptionBehavior.Throw)
        {
            throw new NotSupportedException(
                $"Printer '{configuration.PrinterId}' does not support {String.Join(", ", unsupported)}.");
        }

        dropped = unsupported;
        return Without(options, unsupported);
    }

    // A printer that reported no capability at all cannot say what it does not support.
    private static bool IsEmpty(PrinterConfiguration configuration) =>
        !configuration.SupportsDuplex
        && !configuration.SupportsColor
        && configuration.MediaSizes.Count == 0
        && configuration.SupportedResolutionsDpi.Count == 0;

    // The caller keeps its own instance, so the removal happens on a copy.
    private static PrintOptions Without(PrintOptions options, List<string> unsupported)
    {
        PrintOptions copy = new()
        {
            Copies = options.Copies,
            Duplex = options.Duplex,
            ColorMode = options.ColorMode,
            Orientation = options.Orientation,
            MediaSource = options.MediaSource,
            MediaSize = options.MediaSize,
            ResolutionDpi = options.ResolutionDpi,
            JobName = options.JobName,
            OnUnsupported = options.OnUnsupported,
        };

        foreach (var name in unsupported)
        {
            if (name == nameof(PrintOptions.Duplex))
            {
                copy.Duplex = null;
            }
            else if (name == nameof(PrintOptions.ColorMode))
            {
                copy.ColorMode = null;
            }
            else if (name == nameof(PrintOptions.MediaSize))
            {
                copy.MediaSize = null;
            }
            else if (name == nameof(PrintOptions.ResolutionDpi))
            {
                copy.ResolutionDpi = null;
            }
        }

        return copy;
    }
}
```

`Copies`, `Orientation`, `MediaSource` and `JobName` have no matching capability in `PrinterConfiguration`, so they always pass.

The interface block says `Apply` is `internal`, but the test calls it from the test assembly. That works because `src/Devices` already grants `InternalsVisibleTo` to `AdaptArch.Devices.UnitTests`; confirm that attribute exists before Step 4, and add it if it does not.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrintOptionValidatorTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/PrintOptionValidator.cs test/Devices.UnitTests/Printing/PrintOptionValidatorTests.cs
git commit -m "feat: check print options against the printer configuration"
```

---

### Task 8: IppPrinter

**Files:**
- Create: `src/Devices/Printing/Ipp/IppPrinter.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppPrinterTests.cs`

**Interfaces:**
- Consumes: `IppEndpointResolver`, `IppOperations`, `IppConfigurationMapper`, `IppJobTemplateMapper`, `PrintOptionValidator`.
- Produces: `public sealed class IppPrinter : IPrinter, IDisposable` with the constructors `IppPrinter(NetworkPrinterEndpoint endpoint)` and `IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient)`. It also exposes `internal IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient, string? resourcePath)` for the factory. `GetConfigurationAsync` reads one time and keeps the answer.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppPrinterTests.cs`. Cover: a submitted job returns the job identifier the printer assigned; the document format comes from the payload content type; `Throw` rejects an unsupported option before any print request goes out; `Drop` names the removed option on the returned job; and `GetConfigurationAsync` sends only one attribute request when it is called twice.

```csharp
using System.Net;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppPrinterTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = new("printer.local", 631);

    [Fact]
    public async Task PrintAsync_ReturnsTheJobIdentifierThePrinterAssigned()
    {
        // 0x21 is integer; 0x23 is enum. Job state 3 is pending.
        var job = IppMessages.Response(0x0000, (0x21, "job-id", 42), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(job));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var result = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("42", result.JobId);
        Assert.Equal(PrintJobState.Queued, result.State);
        Assert.Empty(result.DroppedOptions);
    }

    [Fact]
    public async Task PrintAsync_ThrowRejectsAnUnsupportedOptionBeforeItPrints()
    {
        var attributes = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0),
            (0x44, "media-supported", "iso_a4_210x297mm"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.TwoSidedLongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsOneTimeAndKeepsTheAnswer()
    {
        var attributes = IppMessages.Response(0x0000, (0x22, "color-supported", (byte)1));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var first = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);
        var second = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.True(first.SupportsColor);
        Assert.Same(first, second);
        // One probe from the resolver and one attribute read. No second read.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsTheState()
    {
        var attributes = IppMessages.Response(0x0000,
            (0x23, "printer-state", 4),
            (0x44, "printer-state-reasons", "media-empty"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Processing, status.State);
        Assert.Equal("media-empty", status.Detail);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppPrinterTests"`
Expected: the build fails, because `IppPrinter` does not exist.

- [ ] **Step 3: Write IppPrinter**

Create `src/Devices/Printing/Ipp/IppPrinter.cs`. `PrintAsync` builds a `PrintJobRequest` whose `Document` is a `MemoryStream` over the payload, whose `OperationAttributes.DocumentFormat` comes from `PrinterPayload.ContentType`, whose `OperationAttributes.JobName` comes from `PrintOptions.JobName`, and whose `JobTemplateAttributes` comes from `IppJobTemplateMapper`. Reuse the private helper that `IppPrinterStatusClient` uses to build a `PrinterStatus`, or copy it and delete the duplicate in a later cleanup; do not leave two copies at the end of this task. The simplest correct move is to extract the mapping in `IppPrinterStatusClient.CreateDetails` into `IppStatusMapper` in the `Ipp` folder, and call it from both.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppPrinterTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the whole suite**

Run from the repository root: `dotnetup dotnet test`
Expected: PASS. The status-mapper extraction must not change `IppPrinterStatusClientTests`.

- [ ] **Step 6: Commit**

```bash
git add src/Devices/Printing/Ipp/ src/Devices/Printing/IppPrinterStatusClient.cs test/Devices.UnitTests/Printing/Ipp/
git commit -m "feat: print over IPP through IppPrinter"
```

---

### Task 9: RawPrinter and the printer factory

**Files:**
- Create: `src/Devices/Printing/RawPrinter.cs`, `src/Devices/Printing/IPrinterFactory.cs`, `src/Devices/Printing/PrinterFactory.cs`
- Test: `test/Devices.UnitTests/Printing/PrinterFactoryTests.cs`, `test/Devices.UnitTests/Printing/RawPrinterTests.cs`

**Interfaces:**
- Consumes: `IppPrinter` from Task 8, `TcpPrinterTransport`, `SnmpPrinterStatusClient`, `IppPrinterStatusClient`.
- Produces: `public interface IPrinterFactory` with `IPrinter Open(DiscoveredPrinter printer)` and `Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)`; `public sealed class PrinterFactory : IPrinterFactory`; `public sealed class RawPrinter : IPrinter`.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/PrinterFactoryTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterFactoryTests
{
    private static DiscoveredPrinter Found(PrinterEndpoint endpoint) =>
        new(PrinterId.FromNetwork("printer.local"), endpoint, new PrinterInfo(PrinterId.FromNetwork("printer.local"), "Lobby"));

    [Fact]
    public void Open_MakesAnIppPrinterForPort631()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(new NetworkPrinterEndpoint("printer.local", 631)));

        _ = Assert.IsType<IppPrinter>(printer);
    }

    [Fact]
    public void Open_MakesARawPrinterForPort9100()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(new NetworkPrinterEndpoint("printer.local", 9100)));

        _ = Assert.IsType<RawPrinter>(printer);
    }

    [Fact]
    public void Open_ThrowsForAUsbEndpoint()
    {
        PrinterFactory factory = new();
        var discovered = new DiscoveredPrinter(
            PrinterId.FromUsb("usb-1"),
            new UsbPrinterEndpoint(0x04B8, 0x0202),
            new PrinterInfo(PrinterId.FromUsb("usb-1"), "Label printer"));

        var error = Assert.Throws<NotSupportedException>(() => factory.Open(discovered));

        Assert.Contains("USB", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

Create `test/Devices.UnitTests/Printing/RawPrinterTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class RawPrinterTests
{
    [Fact]
    public async Task PrintAsync_ReportsACompletedJobWithAGeneratedIdentifier()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        RawPrinter printer = new(new NetworkPrinterEndpoint("127.0.0.1", port));

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        using var accepted = await accept;
        listener.Stop();
        Assert.NotEmpty(job.JobId);
        // The raw channel gives no job identifier, so the job is done when the bytes are out.
        Assert.Equal(PrintJobState.Completed, job.State);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReportsNothingKnown()
    {
        RawPrinter printer = new(new NetworkPrinterEndpoint("127.0.0.1", 9100));

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.False(configuration.SupportsDuplex);
    }
}
```

Read `test/Devices.UnitTests/Printing/TcpPrinterTransportTests.cs` first, and follow whatever listener pattern it already uses rather than the one above if the two differ.

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrinterFactoryTests"`
Expected: the build fails.

- [ ] **Step 3: Write RawPrinter, IPrinterFactory and PrinterFactory**

`RawPrinter.PrintAsync` writes through `TcpPrinterTransport` and then returns a job whose identifier is `Guid.NewGuid().ToString("n")`, whose state is `Completed`, and whose `CompletedAt` is set, because the raw channel gives no job identifier. `GetStatusAsync` tries SNMP and then IPP, and returns `Unknown` when neither answers. `GetConfigurationAsync` returns `new PrinterConfiguration(Id)`.

`PrinterFactory.Open` reads the endpoint type. A `NetworkPrinterEndpoint` whose `Port` is `IppPrinterStatusClient.DefaultPort` makes an `IppPrinter`; any other network port makes a `RawPrinter`. A `SpoolerPrinterEndpoint` throws `NotSupportedException` for now, with the message "The spooler printer arrives in a later task."; Task 15 replaces that line. A `UsbPrinterEndpoint` throws `NotSupportedException` that names USB.

`PrinterFactory.OpenAsync` for `PrinterIdKind.Network` makes an `IppPrinter` and calls `GetStatusAsync`; when that throws `InvalidOperationException`, it falls back to a `RawPrinter` on port 9100.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PrinterFactoryTests|FullyQualifiedName~RawPrinterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/RawPrinter.cs src/Devices/Printing/IPrinterFactory.cs src/Devices/Printing/PrinterFactory.cs test/Devices.UnitTests/Printing/
git commit -m "feat: open a printer from an endpoint"
```

---

## Phase 3 — Job queue and monitor

### Task 10: The IPP job mapper and job queue

**Files:**
- Create: `src/Devices/Printing/Ipp/IppJobMapper.cs`, `src/Devices/Printing/Ipp/IppPrintJobQueue.cs`
- Test: `test/Devices.UnitTests/Printing/Ipp/IppJobMapperTests.cs`, `test/Devices.UnitTests/Printing/Ipp/IppPrintJobQueueTests.cs`

**Interfaces:**
- Consumes: `PrintJobInfo` from Task 5, `IppOperations` and `IppEndpointResolver`.
- Produces: `internal static class IppJobMapper` with `public static PrintJobInfo Map(PrinterId id, JobDescriptionAttributes attributes)` and `public static PrintJobState MapState(JobState? state)`; `public sealed class IppPrintJobQueue : IPrintJobQueue, IDisposable` with `IppPrintJobQueue(NetworkPrinterEndpoint endpoint, HttpClient httpClient)`.

- [ ] **Step 1: Write the failing state-map test**

Create `test/Devices.UnitTests/Printing/Ipp/IppJobMapperTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol.Models;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppJobMapperTests
{
    [Theory]
    [InlineData(JobState.Pending, PrintJobState.Queued)]
    [InlineData(JobState.PendingHeld, PrintJobState.Paused)]
    [InlineData(JobState.Processing, PrintJobState.Printing)]
    [InlineData(JobState.ProcessingStopped, PrintJobState.Paused)]
    [InlineData(JobState.Completed, PrintJobState.Completed)]
    [InlineData(JobState.Canceled, PrintJobState.Canceled)]
    [InlineData(JobState.Aborted, PrintJobState.Failed)]
    public void MapState_FollowsTheSpecTable(JobState ipp, PrintJobState expected) =>
        Assert.Equal(expected, IppJobMapper.MapState(ipp));

    [Fact]
    public void MapState_ReportsQueuedForNoState() =>
        Assert.Equal(PrintJobState.Queued, IppJobMapper.MapState(null));

    [Fact]
    public void Map_ReadsTheProgressCounters()
    {
        JobDescriptionAttributes attributes = new()
        {
            JobId = 42,
            JobName = "label.zpl",
            JobState = JobState.Processing,
            JobImpressions = 10,
            JobImpressionsCompleted = 4,
            JobStateReasons = [],
        };

        var job = IppJobMapper.Map(PrinterId.FromNetwork("printer.local"), attributes);

        Assert.Equal("42", job.JobId);
        Assert.Equal("label.zpl", job.JobName);
        Assert.Equal(PrintJobState.Printing, job.State);
        Assert.Equal(4, job.ImpressionsCompleted);
        Assert.Equal(10, job.TotalImpressions);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppJobMapperTests"`
Expected: the build fails, because `IppJobMapper` does not exist.

- [ ] **Step 3: Write the mapper**

Create `src/Devices/Printing/Ipp/IppJobMapper.cs`. Write `MapState` as a chain of `if` statements, because IDE0066 turns off switch expressions. Map `JobStateReasons` into `Detail` with the same join rule the status client uses: drop the reason `none`, and join the rest with `"; "`. Set `CreatedAt` from `DateTimeAtCreation` and `CompletedAt` from `DateTimeAtCompleted`.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppJobMapperTests"`
Expected: PASS, 9 test cases.

- [ ] **Step 5: Write the failing queue tests**

Create `test/Devices.UnitTests/Printing/Ipp/IppPrintJobQueueTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppPrintJobQueueTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = new("printer.local", 631);
    private static readonly PrinterId Printer = PrinterId.FromNetwork("printer.local");

    [Fact]
    public async Task GetJobsAsync_ReturnsOneEntryForEachJob()
    {
        // 0x21 is integer, 0x23 is enum, 0x42 is nameWithoutLanguage. A repeated
        // "job-id" name starts a new job group in the answer.
        var body = IppMessages.Response(0x0000,
            (0x21, "job-id", 1),
            (0x23, "job-state", 5),
            (0x42, "job-name", "first.zpl"),
            (0x21, "job-id", 2),
            (0x23, "job-state", 3),
            (0x42, "job-name", "second.zpl"));
        using IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var jobs = await queue.GetJobsAsync(Printer, TestContext.Current.CancellationToken);

        Assert.Equal(2, jobs.Count);
        Assert.Equal("1", jobs[0].JobId);
        Assert.Equal(PrintJobState.Printing, jobs[0].State);
        Assert.Equal("second.zpl", jobs[1].JobName);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenThePrinterDoesNotKnowTheJob()
    {
        // 0x0406 is client-error-not-found.
        var body = IppMessages.Response(0x0406);
        using IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var job = await queue.GetJobAsync(Printer, "9999", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenTheIdentifierIsNotANumber()
    {
        using IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok(IppMessages.Response(0x0000)))));

        var job = await queue.GetJobAsync(Printer, "not-a-number", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task CancelJobAsync_ReportsTrueOnSuccessAndFalseOnNotFound()
    {
        using IppPrintJobQueue ok = new(Endpoint, new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok(IppMessages.Response(0x0000)))));
        using IppPrintJobQueue missing = new(Endpoint, new HttpClient(new IppMessages.StubHandler(
            _ => IppMessages.Ok(IppMessages.Response(0x0406)))));

        Assert.True(await ok.CancelJobAsync(Printer, "1", TestContext.Current.CancellationToken));
        Assert.False(await missing.CancelJobAsync(Printer, "1", TestContext.Current.CancellationToken));
    }
}
```

The first test depends on how `SharpIppNext` groups repeated job attributes in one answer. Run it before you write the queue. If the decoder needs a group separator byte between the jobs, add that byte to `IppMessages.Response` as a new tag entry and say so in a comment.

- [ ] **Step 6: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppPrintJobQueueTests"`
Expected: the build fails, because `IppPrintJobQueue` does not exist.

- [ ] **Step 7: Write the queue**

Create `src/Devices/Printing/Ipp/IppPrintJobQueue.cs`. `GetJobsAsync` sends a `GetJobsRequest` whose `OperationAttributes.WhichJobs` is not set, so the printer reports its default set. `GetJobAsync` parses the job identifier with `Int32.TryParse` and returns `null` when the text is not a number, because a caller may hold an identifier from another source. A `not-found` answer arrives as `InvalidOperationException` from `IppOperations`; catch it, and return `null` or `false` rather than let it escape.

- [ ] **Step 8: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~IppPrintJobQueueTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Devices/Printing/Ipp/IppJobMapper.cs src/Devices/Printing/Ipp/IppPrintJobQueue.cs test/Devices.UnitTests/Printing/Ipp/
git commit -m "feat: read the IPP job queue"
```

---

### Task 11: The polling job monitor

**Files:**
- Create: `src/Devices/Printing/IPrintJobMonitor.cs`, `src/Devices/Printing/PrintJobMonitorOptions.cs`, `src/Devices/Printing/PollingPrintJobMonitor.cs`
- Test: `test/Devices.UnitTests/Printing/PollingPrintJobMonitorTests.cs`

**Interfaces:**
- Consumes: `IPrintJobQueue`, `PrintJobInfo` from Task 5.
- Produces: `public interface IPrintJobMonitor` with `IAsyncEnumerable<PrintJobInfo> WatchJobAsync(PrinterId printerId, string jobId, PrintJobMonitorOptions options, CancellationToken cancellationToken)`; `public sealed class PrintJobMonitorOptions` with `TimeSpan PollInterval { get; set; }` defaulting to one second and `TimeSpan? Timeout { get; set; }` defaulting to `null`; `public sealed class PollingPrintJobMonitor : IPrintJobMonitor` with `PollingPrintJobMonitor(IPrintJobQueue queue)` and `PollingPrintJobMonitor(IPrintJobQueue queue, TimeProvider timeProvider)`.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/PollingPrintJobMonitorTests.cs`. Use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` only if it is already a dependency; it is not, so write a small fake queue and drive the clock with a `PollInterval` of `TimeSpan.FromMilliseconds(1)`. That keeps the test fast without a new package.

```csharp
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PollingPrintJobMonitorTests
{
    private static readonly PrinterId Printer = PrinterId.FromNetwork("printer.local");
    private static readonly PrintJobMonitorOptions Fast = new() { PollInterval = TimeSpan.FromMilliseconds(1) };

    [Fact]
    public async Task WatchJobAsync_YieldsEachChangeAndStopsAtATerminalState()
    {
        FakeQueue queue = new(
            Job(PrintJobState.Queued, null),
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Printing, 2),
            Job(PrintJobState.Completed, 2));
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(4, seen.Count);
        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_DoesNotRepeatAnUnchangedReading()
    {
        FakeQueue queue = new(
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Completed, 1));
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task WatchJobAsync_ReportsCompletedWhenTheJobLeavesTheQueue()
    {
        // Both CUPS and the Windows spooler drop a finished job, so an absent job is done.
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), null);
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_StopsWhenTheCallerCancels()
    {
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), Job(PrintJobState.Printing, 2));
        PollingPrintJobMonitor monitor = new(queue);
        using CancellationTokenSource source = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, source.Token))
            {
                await source.CancelAsync();
            }
        });
    }

    private static PrintJobInfo Job(PrintJobState state, int? done) =>
        new("42", Printer, state) { ImpressionsCompleted = done };

    private sealed class FakeQueue : IPrintJobQueue
    {
        private readonly Queue<PrintJobInfo?> _readings;

        public FakeQueue(params PrintJobInfo?[] readings) => _readings = new Queue<PrintJobInfo?>(readings);

        public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
            Task.FromResult(_readings.Count > 0 ? _readings.Dequeue() : null);

        public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PollingPrintJobMonitorTests"`
Expected: the build fails, because the monitor types do not exist.

- [ ] **Step 3: Write the three types**

`IPrintJobMonitor` and `PrintJobMonitorOptions` are plain declarations; write them from the interface block above, with XML documentation on every public member.

Create `src/Devices/Printing/PollingPrintJobMonitor.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Watches a print job by reading the job queue again and again. This works with every
/// printer, because it needs no notification channel.
/// </summary>
public sealed class PollingPrintJobMonitor : IPrintJobMonitor
{
    private readonly IPrintJobQueue _queue;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingPrintJobMonitor"/> class.
    /// </summary>
    /// <param name="queue">The queue to read.</param>
    public PollingPrintJobMonitor(IPrintJobQueue queue)
        : this(queue, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingPrintJobMonitor"/> class with
    /// a caller-provided clock. Tests supply a clock so that no test waits.
    /// </summary>
    /// <param name="queue">The queue to read.</param>
    /// <param name="timeProvider">The clock used between reads.</param>
    public PollingPrintJobMonitor(IPrintJobQueue queue, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _queue = queue;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId printerId,
        string jobId,
        PrintJobMonitorOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(options);

        // A timeout ends the watch quietly. Only the caller's token throws.
        using CancellationTokenSource deadline = new();
        if (options.Timeout is TimeSpan limit)
        {
            deadline.CancelAfter(limit, _timeProvider);
        }

        PrintJobState? lastState = null;
        int? lastCount = null;
        while (!deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = await _queue.GetJobAsync(printerId, jobId, cancellationToken).ConfigureAwait(false);
            if (reading is null)
            {
                // Both CUPS and the Windows spooler drop a finished job from the queue, so
                // a job that is gone has finished.
                PrintJobInfo done = new(jobId, printerId, PrintJobState.Completed)
                {
                    CompletedAt = _timeProvider.GetUtcNow(),
                };
                yield return done;
                yield break;
            }

            if (reading.State != lastState || reading.ImpressionsCompleted != lastCount)
            {
                lastState = reading.State;
                lastCount = reading.ImpressionsCompleted;
                yield return reading;
            }

            if (IsTerminal(reading.State))
            {
                yield break;
            }

            await Task.Delay(options.PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTerminal(PrintJobState state) =>
        state == PrintJobState.Completed || state == PrintJobState.Failed || state == PrintJobState.Canceled;
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~PollingPrintJobMonitorTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/IPrintJobMonitor.cs src/Devices/Printing/PrintJobMonitorOptions.cs src/Devices/Printing/PollingPrintJobMonitor.cs test/Devices.UnitTests/Printing/PollingPrintJobMonitorTests.cs
git commit -m "feat: watch a print job to completion"
```

---

### Task 12: The sample watch command

**Files:**
- Modify: `samples/Devices.Samples/Program.cs`

**Interfaces:**
- Consumes: `IPrinterFactory`, `IPrintJobMonitor` from Tasks 9 and 11.
- Produces: nothing that another task uses.

- [ ] **Step 1: Add the command**

Read `samples/Devices.Samples/Program.cs` first, and follow the shape of the existing `--send` branch, including its `Confirm` gate. Printing costs paper and ink, so the new command must ask before it sends.

Add a branch for `args.Length == 3 && args[0] == "--watch"`. It opens the printer with `IPrinterFactory.OpenAsync`, submits the named file from `PrintFiles`, then writes one line for each reading from `IPrintJobMonitor.WatchJobAsync`.

```csharp
if (args.Length == 3 && args[0] == "--watch")
{
    if (!Confirm($"Send '{args[2]}' to printer {args[1]} and watch the job?"))
    {
        Console.WriteLine("Cancelled; nothing was sent.");
        return;
    }

    var factory = provider.GetRequiredService<IPrinterFactory>();
    var monitor = provider.GetRequiredService<IPrintJobMonitor>();
    var printer = await factory.OpenAsync(PrinterId.FromNetwork(args[1]), CancellationToken.None).ConfigureAwait(false);
    var bytes = await File.ReadAllBytesAsync(Path.Combine(printFilesDirectory, args[2])).ConfigureAwait(false);
    var submitted = await printer.PrintAsync(
        PrinterPayload.FromBytes(bytes, PrinterContentTypes.OctetStream),
        new PrintOptions { JobName = args[2] },
        CancellationToken.None).ConfigureAwait(false);

    Console.WriteLine($"Job {submitted.JobId} submitted.");
    await foreach (var reading in monitor.WatchJobAsync(
        PrinterId.FromNetwork(args[1]), submitted.JobId, new PrintJobMonitorOptions(), CancellationToken.None).ConfigureAwait(false))
    {
        var progress = reading.TotalImpressions is int total
            ? $" {reading.ImpressionsCompleted ?? 0}/{total} pages"
            : String.Empty;
        Console.WriteLine($"  {reading.State}{progress} {reading.Detail}");
    }

    return;
}
```

Update the closing help line so it names `--watch` beside `--send`.

- [ ] **Step 2: Build the sample**

Run from the repository root: `dotnetup dotnet build`
Expected: the build succeeds with no warning.

- [ ] **Step 3: Commit**

```bash
git add samples/Devices.Samples/Program.cs
git commit -m "docs: show live job progress in the sample"
```

---

## Phase 4 — The spooler

### Task 13: ISpoolerDriver and the CUPS driver

**Files:**
- Create: `src/Devices/Printing/Spooler/ISpoolerDriver.cs`, `src/Devices/Printing/Spooler/CupsSpoolerDriver.cs`
- Test: `test/Devices.UnitTests/Printing/Spooler/CupsSpoolerDriverTests.cs`

**Interfaces:**
- Consumes: `IppOperations`, `IppJobMapper`, `IppConfigurationMapper`, `IppJobTemplateMapper`.
- Produces: `internal interface ISpoolerDriver` exactly as the spec declares it in section 4.1; `internal sealed class CupsSpoolerDriver : ISpoolerDriver` with `CupsSpoolerDriver(HttpClient httpClient)` and an internal constructor `CupsSpoolerDriver(HttpClient httpClient, Uri baseUri)` so a test can point it at a stub.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Spooler/CupsSpoolerDriverTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class CupsSpoolerDriverTests
{
    [Fact]
    public async Task EnumeratePrintersAsync_ReportsEachQueueAsASpoolerEndpoint()
    {
        var body = IppMessages.Response(0x0000,
            (0x42, "printer-name", "lobby"),
            (0x42, "printer-info", "Lobby LaserJet"),
            (0x42, "printer-location", "Reception"));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var printers = await driver.EnumeratePrintersAsync(TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal(PrinterIdKind.Spooler, printer.Id.Kind);
        Assert.Equal("lobby", printer.Id.Value);
        var endpoint = Assert.IsType<SpoolerPrinterEndpoint>(printer.Endpoint);
        Assert.Equal("lobby", endpoint.Name);
    }

    [Fact]
    public async Task SubmitAsync_SendsToTheQueueUriOnTheLocalDaemon()
    {
        var body = IppMessages.Response(0x0000, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("7", job.JobId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("localhost", request.RequestUri.Host);
        Assert.Equal(631, request.RequestUri.Port);
        Assert.Equal("/printers/lobby", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetJobsAsync_MapsEachJob()
    {
        var body = IppMessages.Response(0x0000,
            (0x21, "job-id", 7),
            (0x23, "job-state", 5),
            (0x21, "job-impressions", 4),
            (0x21, "job-impressions-completed", 1));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var jobs = await driver.GetJobsAsync("lobby", TestContext.Current.CancellationToken);

        var job = Assert.Single(jobs);
        Assert.Equal(PrintJobState.Printing, job.State);
        Assert.Equal(1, job.ImpressionsCompleted);
        Assert.Equal(4, job.TotalImpressions);
    }
}
```

`CupsSpoolerDriver` is `internal`, so this test needs the `InternalsVisibleTo` attribute that Task 14 Step 1 checks for. Confirm it before Step 2.

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~CupsSpoolerDriverTests"`
Expected: the build fails, because the driver does not exist.

- [ ] **Step 3: Write the interface and the CUPS driver**

Create `src/Devices/Printing/Spooler/ISpoolerDriver.cs` with the seven methods from the spec, all `internal`.

Create `src/Devices/Printing/Spooler/CupsSpoolerDriver.cs`. The base URI is `ipp://localhost:631/`. A queue named `lobby` is reached at `ipp://localhost:631/printers/lobby`. Enumeration sends `CUPSGetPrintersRequest` with `OperationAttributes.PrinterUri` set to `ipp://localhost:631/` and reads `CUPSGetPrintersResponse.PrintersAttributes`. The identity of each queue is `PrinterId.FromSpooler(attributes.PrinterName)`. No probe is needed, because the daemon path is fixed; do not use `IppEndpointResolver` here.

The document format follows the payload content type, as in `IppPrinter`. CUPS accepts `application/octet-stream` and applies its own filter chain.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~CupsSpoolerDriverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Devices/Printing/Spooler/ test/Devices.UnitTests/Printing/Spooler/
git commit -m "feat: reach the CUPS spooler over local IPP"
```

---

### Task 14: The Windows spooler driver

**Files:**
- Create: `src/Devices/Printing/Spooler/WindowsSpoolerInterop.cs`, `src/Devices/Printing/Spooler/WindowsSpoolerDriver.cs`, `src/Devices/Printing/Spooler/SpoolerDriverFactory.cs`
- Test: `test/Devices.UnitTests/Printing/Spooler/SpoolerDriverFactoryTests.cs`

**Interfaces:**
- Consumes: `ISpoolerDriver` from Task 13.
- Produces: `internal static partial class WindowsSpoolerInterop` holding only `LibraryImport` declarations and structures; `internal sealed class WindowsSpoolerDriver : ISpoolerDriver`; `internal static class SpoolerDriverFactory` with `public static ISpoolerDriver Create(HttpClient httpClient)`.

- [ ] **Step 1: Write the failing factory test**

Create `test/Devices.UnitTests/Printing/Spooler/SpoolerDriverFactoryTests.cs`:

```csharp
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class SpoolerDriverFactoryTests
{
    [Fact]
    public void Create_SelectsTheDriverForThisOperatingSystem()
    {
        var driver = SpoolerDriverFactory.Create(new HttpClient());

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("WindowsSpoolerDriver", driver.GetType().Name);
        }
        else
        {
            Assert.Equal("CupsSpoolerDriver", driver.GetType().Name);
        }
    }
}
```

The test project must see internal types. Add `[assembly: InternalsVisibleTo("AdaptArch.Devices.UnitTests")]` to `src/Devices/` if it is not there already; check first, because the existing `IUdpChannel` tests suggest it is.

- [ ] **Step 2: Run the test to see it fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~SpoolerDriverFactoryTests"`
Expected: the build fails, because the factory does not exist.

- [ ] **Step 3: Write the interop declarations**

Create `src/Devices/Printing/Spooler/WindowsSpoolerInterop.cs`. Use `LibraryImport`, never `DllImport`, because `DllImport` needs a marshalling stub that native AOT cannot generate. Mark the class `partial`, which the source generator needs. Guard the whole file with `[SupportedOSPlatform("windows")]`.

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// The Windows print spooler API. Only the entry points this library calls are declared.
// LibraryImport is a source generator, so no reflection reaches the trimmed output.
[SupportedOSPlatform("windows")]
internal static partial class WindowsSpoolerInterop
{
    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenPrinter(string printerName, out nint printerHandle, nint defaults);

    [LibraryImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClosePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true)]
    internal static partial int StartDocPrinter(nint printerHandle, int level, in DocInfo1 documentInfo);

    [LibraryImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndDocPrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool StartPagePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPagePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WritePrinter(nint printerHandle, nint buffer, int count, out int written);

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumPrinters(int flags, nint name, int level, nint buffer, int bufferSize, out int needed, out int returned);

    [LibraryImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumJobs(nint printerHandle, int firstJob, int jobCount, int level, nint buffer, int bufferSize, out int needed, out int returned);

    [LibraryImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetJob(nint printerHandle, int jobId, int level, nint job, int command);

    [LibraryImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int DeviceCapabilities(string device, string? port, short capability, nint output, nint deviceMode);

    // JOB_CONTROL_CANCEL
    internal const int JobControlCancel = 3;

    // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
    internal const int PrinterEnumLocalAndConnections = 0x00000006;

    internal const short DcPaperNames = 16;
    internal const short DcDuplex = 7;
    internal const short DcColorDevice = 32;
    internal const short DcEnumResolutions = 13;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DocInfo1
    {
        internal nint DocName;
        internal nint OutputFile;
        internal nint DataType;
    }
}
```

Add the `PRINTER_INFO_4` and `JOB_INFO_2` structures the driver reads. Declare only the fields the driver uses, and pad the rest with the correct offsets; a wrong layout gives silent memory corruption, so copy the field order from the Windows headers exactly.

- [ ] **Step 4: Write the driver**

Create `src/Devices/Printing/Spooler/WindowsSpoolerDriver.cs`. Every method starts with a platform check:

```csharp
if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
}
```

A failed call reads `Marshal.GetLastWin32Error()` and throws `InvalidOperationException` whose message carries the code. Free every buffer in a `finally` block, and close every printer handle in a `finally` block.

**Submission.** The data type is `RAW`, because this library sends printer languages such as ZPL and ESC/POS. The order is `OpenPrinter`, `StartDocPrinter`, `StartPagePrinter`, `WritePrinter`, `EndPagePrinter`, `EndDocPrinter`, `ClosePrinter`. `DOC_INFO_1.pDocName` carries `PrintOptions.JobName`. The returned job identifier is the value `StartDocPrinter` gives back.

**The other print options.** Copies, duplex, colour, orientation, media source and media size belong in a `DEVMODE` passed to `OpenPrinter` through `PRINTER_DEFAULTS`. A `RAW` job reaches the device unchanged, so a `DEVMODE` changes the result only when the queue has a driver that reads it. Map the options into `DEVMODE` as spec section 5.1 states, and add a comment in the code that says the driver may ignore them. This matches `UnsupportedOptionBehavior.Send`: the request goes out and the printer decides.

**Configuration.** `GetConfigurationAsync` calls `DeviceCapabilities` four times, with `DcEnumResolutions`, `DcDuplex`, `DcColorDevice` and `DcPaperNames`. Each call is made twice: the first with a null buffer to learn the count, the second with a buffer of that size. `DcDuplex` and `DcColorDevice` return 1 or 0 and need no buffer. `DcEnumResolutions` returns pairs of `int`, of which the first is the horizontal count in dots per inch. `DcPaperNames` returns fixed 64-character blocks; trim each at the first null character.

**Status.** `GetStatusAsync` calls `GetPrinter` at level 2 and reads the `Status` field. Map `PRINTER_STATUS_PAUSED` to `Paused`, `PRINTER_STATUS_OFFLINE` and `PRINTER_STATUS_NOT_AVAILABLE` to `Offline`, `PRINTER_STATUS_ERROR` and the paper and door bits to `Error`, `PRINTER_STATUS_PRINTING` and `PRINTER_STATUS_PROCESSING` to `Processing`, and zero to `Idle`.

- [ ] **Step 5: Write the factory**

```csharp
internal static class SpoolerDriverFactory
{
    public static ISpoolerDriver Create(HttpClient httpClient) =>
        OperatingSystem.IsWindows()
            ? new WindowsSpoolerDriver()
            : new CupsSpoolerDriver(httpClient);
}
```

- [ ] **Step 6: Run the test to see it pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~SpoolerDriverFactoryTests"`
Expected: PASS on Linux, which selects `CupsSpoolerDriver`.

- [ ] **Step 7: Confirm the AOT rules hold**

Run from the repository root: `dotnetup dotnet build -c Release`
Expected: the build succeeds with no warning. `src/` treats a trim or AOT warning as an error, so a `DllImport` or a bad marshalling attribute fails here.

- [ ] **Step 8: Commit**

```bash
git add src/Devices/Printing/Spooler/ test/Devices.UnitTests/Printing/Spooler/
git commit -m "feat: reach the Windows print spooler"
```

---

### Task 15: SpoolerPrinter, spooler discovery and the composite queue

**Files:**
- Create: `src/Devices/Printing/Spooler/SpoolerPrinter.cs`, `src/Devices/Printing/Spooler/SpoolerPrinterDiscovery.cs`, `src/Devices/Printing/Spooler/SpoolerPrintJobQueue.cs`, `src/Devices/Printing/CompositePrintJobQueue.cs`
- Modify: `src/Devices/Printing/PrinterFactory.cs`
- Test: `test/Devices.UnitTests/Printing/Spooler/SpoolerPrinterTests.cs`, `test/Devices.UnitTests/Printing/CompositePrintJobQueueTests.cs`

**Interfaces:**
- Consumes: `ISpoolerDriver` from Task 13, `IppPrintJobQueue` from Task 10.
- Produces: `public sealed class SpoolerPrinter : IPrinter`; `public sealed class SpoolerPrinterDiscovery : IPrinterDiscovery`; `public sealed class SpoolerPrintJobQueue : IPrintJobQueue`; `public sealed class CompositePrintJobQueue : IPrintJobQueue` with `CompositePrintJobQueue(SpoolerPrintJobQueue spoolerQueue, HttpClient httpClient)`.
- Every one of these four types also needs a public parameterless constructor that builds its own driver with `SpoolerDriverFactory.Create(new HttpClient())`, because the container registers them with no factory delegate. `SpoolerPrintJobQueue` is the concrete parameter of `CompositePrintJobQueue`; taking `IPrintJobQueue` there would make the composite depend on itself and the container would throw at the first resolve.

- [ ] **Step 1: Write the failing tests**

Create `test/Devices.UnitTests/Printing/Spooler/FakeSpoolerDriver.cs`, in the style of `FakeUdpChannel`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// Records what the printer asked for and returns scripted answers, so a test never needs
// a real spooler.
internal sealed class FakeSpoolerDriver : ISpoolerDriver
{
    private readonly PrinterConfiguration _configuration;

    public FakeSpoolerDriver(PrinterConfiguration configuration) => _configuration = configuration;

    public List<string> SubmittedQueues { get; } = [];

    public List<PrinterPayload> SubmittedPayloads { get; } = [];

    public int ConfigurationReads { get; private set; }

    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        SubmittedQueues.Add(queueName);
        SubmittedPayloads.Add(payload);
        return Task.FromResult(new PrintJobInfo("11", PrinterId.FromSpooler(queueName), PrintJobState.Queued));
    }

    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ConfigurationReads += 1;
        return Task.FromResult(_configuration);
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterStatus(PrinterId.FromSpooler(queueName), PrinterStatusState.Idle));

    public Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiscoveredPrinter>>([]);

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJobInfo>>([]);

    public Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken) =>
        Task.FromResult<PrintJobInfo?>(null);

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
```

Create `test/Devices.UnitTests/Printing/Spooler/SpoolerPrinterTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class SpoolerPrinterTests
{
    private static readonly SpoolerPrinterEndpoint Endpoint = new("lobby");

    private static PrinterConfiguration Simplex() =>
        new(PrinterId.FromSpooler("lobby")) { SupportsDuplex = false, MediaSizes = ["iso_a4_210x297mm"] };

    [Fact]
    public async Task PrintAsync_PassesTheQueueNameAndThePayloadToTheDriver()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);
        var payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        var job = await printer.PrintAsync(payload, null, TestContext.Current.CancellationToken);

        Assert.Equal("lobby", Assert.Single(driver.SubmittedQueues));
        Assert.Same(payload, Assert.Single(driver.SubmittedPayloads));
        Assert.Equal("11", job.JobId);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsOneTimeAndKeepsTheAnswer()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);

        var first = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);
        var second = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, driver.ConfigurationReads);
    }

    [Fact]
    public async Task PrintAsync_ThrowStopsTheJobBeforeItReachesTheDriver()
    {
        FakeSpoolerDriver driver = new(Simplex());
        SpoolerPrinter printer = new(Endpoint, driver);

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.TwoSidedLongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw },
            TestContext.Current.CancellationToken));

        Assert.Empty(driver.SubmittedQueues);
    }
}
```

`SpoolerPrinter` therefore needs an internal constructor `SpoolerPrinter(SpoolerPrinterEndpoint endpoint, ISpoolerDriver driver)` beside its public one, in the same way `MdnsPrinterDiscovery` takes an `IMdnsChannelFactory`.

Create `test/Devices.UnitTests/Printing/CompositePrintJobQueueTests.cs`:

```csharp
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class CompositePrintJobQueueTests
{
    [Fact]
    public async Task CancelJobAsync_SendsASpoolerIdentifierToTheSpoolerQueue()
    {
        FakeSpoolerDriver driver = new(new PrinterConfiguration(PrinterId.FromSpooler("lobby")));
        CompositePrintJobQueue queue = new(new SpoolerPrintJobQueue(driver), new HttpClient());

        var cancelled = await queue.CancelJobAsync(
            PrinterId.FromSpooler("lobby"), "11", TestContext.Current.CancellationToken);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task GetJobsAsync_RejectsAUsbIdentifier()
    {
        FakeSpoolerDriver driver = new(new PrinterConfiguration(PrinterId.FromSpooler("lobby")));
        CompositePrintJobQueue queue = new(new SpoolerPrintJobQueue(driver), new HttpClient());

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => queue.GetJobsAsync(
            PrinterId.FromUsb("usb-1"), TestContext.Current.CancellationToken));
    }
}
```

`SpoolerPrintJobQueue` therefore needs an internal constructor that takes an `ISpoolerDriver`, beside the public parameterless one the container uses.

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~SpoolerPrinterTests|FullyQualifiedName~CompositePrintJobQueueTests"`
Expected: the build fails.

- [ ] **Step 3: Write the four types**

`SpoolerPrinter` holds an `ISpoolerDriver` and the queue name from its `SpoolerPrinterEndpoint`. It runs `PrintOptionValidator` before it calls the driver, in the same order `IppPrinter` does.

`CompositePrintJobQueue` reads `PrinterId.Kind`. `Spooler` goes to the `SpoolerPrintJobQueue` it was given. `Network` makes an `IppPrintJobQueue` for that host. `Usb` throws `NotSupportedException`.

- [ ] **Step 4: Replace the placeholder in PrinterFactory**

In `PrinterFactory.Open`, replace the `NotSupportedException` for `SpoolerPrinterEndpoint` from Task 9 with a real `SpoolerPrinter`. In `OpenAsync`, make `PrinterIdKind.Spooler` return a `SpoolerPrinter` for the named queue.

- [ ] **Step 5: Run the tests to see them pass**

Run from `test/Devices.UnitTests`: `dotnetup dotnet test --filter "FullyQualifiedName~SpoolerPrinterTests|FullyQualifiedName~CompositePrintJobQueueTests|FullyQualifiedName~PrinterFactoryTests"`
Expected: PASS. `PrinterFactoryTests` runs here because Step 4 changed its subject; add a test there that proves a `SpoolerPrinterEndpoint` now makes a `SpoolerPrinter`.

- [ ] **Step 6: Commit**

```bash
git add src/Devices/Printing/ test/Devices.UnitTests/Printing/
git commit -m "feat: print through the operating system spooler"
```

---

### Task 16: Registrations and documentation

**Files:**
- Modify: `src/Devices.DependencyInjection/ServiceCollectionExtensions.cs`, `docs/printers.md`, `docs/capabilities.md`
- Test: `test/Devices.DependencyInjection.UnitTests/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: every public type from Tasks 9, 11 and 15.
- Produces: nothing that another task uses.

- [ ] **Step 1: Write the failing registration tests**

Read `test/Devices.DependencyInjection.UnitTests/ServiceCollectionExtensionsTests.cs` first and follow its existing shape. Add:

```csharp
[Fact]
public void AddPrinters_ResolvesTheNewPrintingServices()
{
    ServiceCollection services = new();

    _ = services.AddPrinters();

    using var provider = services.BuildServiceProvider();
    _ = Assert.IsType<PrinterFactory>(provider.GetRequiredService<IPrinterFactory>());
    _ = Assert.IsType<SpoolerPrinterDiscovery>(provider.GetRequiredService<IPrinterDiscovery>());
    _ = Assert.IsType<CompositePrintJobQueue>(provider.GetRequiredService<IPrintJobQueue>());
    _ = Assert.IsType<PollingPrintJobMonitor>(provider.GetRequiredService<IPrintJobMonitor>());
}

[Fact]
public void AddPrinters_HoldsTheNewServicesAsSingletons()
{
    ServiceCollection services = new();

    _ = services.AddPrinters();

    using var provider = services.BuildServiceProvider();
    Assert.Same(provider.GetRequiredService<IPrinterFactory>(), provider.GetRequiredService<IPrinterFactory>());
    Assert.Same(provider.GetRequiredService<IPrintJobQueue>(), provider.GetRequiredService<IPrintJobQueue>());
    Assert.Same(provider.GetRequiredService<IPrintJobMonitor>(), provider.GetRequiredService<IPrintJobMonitor>());
}
```

`BuildServiceProvider` resolving `IPrintJobQueue` is what proves the composite does not depend on itself. A cycle throws here.

- [ ] **Step 2: Run the tests to see them fail**

Run from `test/Devices.DependencyInjection.UnitTests`: `dotnetup dotnet test`
Expected: FAIL, because the four services are not registered.

- [ ] **Step 3: Add the registrations**

```csharp
services.TryAddSingleton(TimeProvider.System);
services.AddSingleton<IPrinterFactory, PrinterFactory>();
services.AddSingleton<IPrinterDiscovery, SpoolerPrinterDiscovery>();
services.AddSingleton<SpoolerPrintJobQueue>();
services.AddSingleton<IPrintJobQueue, CompositePrintJobQueue>();
services.AddSingleton<IPrintJobMonitor, PollingPrintJobMonitor>();
```

`TryAddSingleton` for `TimeProvider` lets a host that already registered one keep it. Add `using Microsoft.Extensions.DependencyInjection.Extensions;`.

`SpoolerPrintJobQueue` is registered as its own concrete type as well, because `CompositePrintJobQueue` takes it by that type. Registering it as `IPrintJobQueue` too would make the container hand the composite to itself.

`CompositePrintJobQueue` also needs an `HttpClient`. Register it with a factory rather than a bare type, so the client comes from one place:

```csharp
services.AddSingleton<IPrintJobQueue>(provider => new CompositePrintJobQueue(
    provider.GetRequiredService<SpoolerPrintJobQueue>(),
    new HttpClient()));
```

Use this factory form in place of the plain `AddSingleton<IPrintJobQueue, CompositePrintJobQueue>()` line above.

- [ ] **Step 4: Run the tests to see them pass**

Run from `test/Devices.DependencyInjection.UnitTests`: `dotnetup dotnet test`
Expected: PASS.

- [ ] **Step 5: Update the documentation**

In `docs/printers.md`:

- Delete "OS spooler enumeration", "job queue" and "USB transport" from **Not Yet Implemented**; leave USB and SNMPv3 there, and add "IPP notifications" and "an LPD transport".
- Add a **Printing a job** section that shows `IPrinterFactory`, `IPrinter.PrintAsync`, `PrintOptions.OnUnsupported` and the three behaviours.
- Add a **Job queues and progress** section that shows `IPrintJobQueue` and `IPrintJobMonitor.WatchJobAsync`.
- Add a **Spooler** section that names the two drivers and says that CUPS is reached over local IPP.
- Say that an empty `PrinterConfiguration` means "not known", not "not supported", and that `Throw` and `Drop` then act as `Send`.
- Correct these existing errors, which the gap review found: `PrinterContentTypes` also has `Png` (`image/png`) and `Pdf` (`application/pdf`); `IppPrinterStatusClient` is `IDisposable`; the two status clients have no interface, so a test cannot mock them; `DiscoveredPrinter` pairs `Id`, `Endpoint` and `Info`; `TcpPrinterTransport` has a five second default connect timeout; the registered services are safe to share, which is why the container holds them as singletons.

In `docs/capabilities.md`, rewrite the **Printers (abstractions)** entry so it no longer claims that unimplemented parts exist, and move spooler enumeration, the job queue and IPP printing from the "not yet implemented" sentence into the list of what works.

- [ ] **Step 6: Run the whole suite and the formatter**

Run from the repository root:

```bash
dotnetup dotnet format --verify-no-changes
dotnetup dotnet build -c Release
dotnetup dotnet test
```

Expected: all three succeed with no warning and no failing test.

- [ ] **Step 7: Commit**

```bash
git add src/Devices.DependencyInjection/ServiceCollectionExtensions.cs test/Devices.DependencyInjection.UnitTests/ docs/printers.md docs/capabilities.md
git commit -m "feat: register the printing services and update the documentation"
```

---

## Verification of the whole plan

After Task 16, these statements must all be true:

- `dotnetup dotnet test` passes from the repository root.
- `dotnetup dotnet build -c Release` gives no warning, which proves the trim and AOT rules hold.
- `dotnetup dotnet format --verify-no-changes` reports no change.
- A caller can go from `MdnsPrinterDiscovery` to `IPrinterFactory.Open` to `IPrinter.PrintAsync` to `IPrintJobMonitor.WatchJobAsync` with no other type.
- `PrintOptions`, `PrinterConfiguration`, `PrintJobInfo`, `DuplexMode`, `PrintColorMode` and `PrintOrientation` all have a producer and a consumer.
- `docs/printers.md` describes only behaviour that exists.
