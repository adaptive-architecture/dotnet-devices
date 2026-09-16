using System.Runtime.CompilerServices;
using System.Text;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptArch.Devices.Samples.Api;

// The seams the manager sits on, and the Windows spooler checks. This is the layer a
// person reaches for when the Print tab behaved in a way they did not expect.
internal static class DiagnosticsApi
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Correlation needs a manager of its own: the policy is read from the options the
        // manager was built with. Opening a session to every printer — and, with a tracer,
        // writing to one — is consent that belongs to whoever built the manager.
        _ = app.MapPost("/api/diagnostics/correlate", (CorrelateRequest request, HttpContext context) =>
            TypedResults.ServerSentEvents(CorrelateAsync(request?.Tracer ?? false, context.RequestAborted), "log"));

        _ = app.MapGet("/api/diagnostics/details", async (
            string host,
            IppPrinterStatusClient ipp,
            SnmpPrinterStatusClient snmp,
            CancellationToken cancellationToken) =>
        {
            if (String.IsNullOrWhiteSpace(host))
            {
                return Results.BadRequest("The request names no host.");
            }

            var ippLine = await IppAsync(ipp, host, cancellationToken).ConfigureAwait(false);
            var snmpLine = await SnmpAsync(snmp, host, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new HostDetailsDto(host, ippLine, snmpLine));
        });

        _ = app.MapPost("/api/diagnostics/windows-spooler", (WindowsSpoolerRequest request, HttpContext context) =>
        {
            if (request is null || String.IsNullOrWhiteSpace(request.Queue))
            {
                return Results.BadRequest("The request names no print queue.");
            }

            return TypedResults.ServerSentEvents(
                WindowsSpoolerChecks.RunAsync(request.Queue, request.Print, context.RequestAborted), "log");
        });
    }

    // Proves that two channels reach one queue by comparing the jobs each reports.
    private static async IAsyncEnumerable<LogLineDto> CorrelateAsync(
        bool allowTracer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ServiceCollection services = new();
        _ = services.AddPrinters(configureManager: options =>
        {
            options.ReadIdentity = true;
            options.QueueCorrelation = new QueueCorrelationOptions { AllowTracerJob = allowTracer };
        });

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IPrinterManager>();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(60));

        yield return LogLineDto.Say(allowTracer
            ? "Comparing job queues, and creating a tracer job where a queue is empty..."
            : "Comparing the job queues of the printers that report no identity...");

        IReadOnlyList<PrinterDevice> devices = null;
        string problem = null;
        try
        {
            devices = await manager.DiscoverAsync(null, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PrinterDiscoveryException or OperationCanceledException)
        {
            problem = exception.Message;
        }

        if (problem is not null)
        {
            yield return LogLineDto.Fail($"The discovery failed: {problem}");
            yield break;
        }

        if (devices.Count == 0)
        {
            yield return LogLineDto.Warn("No printers found on the local network.");
            yield break;
        }

        foreach (var device in devices)
        {
            yield return LogLineDto.Say($"- {device.Details.Name} — {device.Key.Value}");
            foreach (var channel in device.Channels)
            {
                yield return LogLineDto.Say($"    {channel.Endpoint.Scheme}: {channel.Id}");
            }

            if (device.Channels.Count > 1 && !device.Key.IsDeviceIdentity)
            {
                // No source named this device, so the queue is what put these channels
                // together. Note it says one queue, and not one sheet-feeding mechanism.
                yield return new LogLineDto("    merged by: the job queue", LogLineDto.Done);
            }
        }
    }

    private static async Task<string> IppAsync(IppPrinterStatusClient client, string host, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var details = await client.GetDetailsAsync(host, timeoutSource.Token).ConfigureAwait(false);
            StringBuilder line = new($"{details.Info.Name} — {details.Status.State}");
            if (details.Status.Detail is not null)
            {
                line.Append($"; {details.Status.Detail}");
            }

            SampleHelpers.AppendMarkers(line, details.Status.Markers);
            return line.ToString();
        }
        catch (Exception exception)
        {
            return $"unavailable: {exception.Message}";
        }
    }

    // SNMP reaches printers that do not answer IPP, and adds the serial and the page count.
    private static async Task<string> SnmpAsync(SnmpPrinterStatusClient client, string host, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var details = await client.GetDetailsAsync(host, timeoutSource.Token).ConfigureAwait(false);
            StringBuilder line = new($"{details.Info.Name} — {details.Status.State}");
            if (details.Status.SerialNumber is not null)
            {
                line.Append($"; serial {details.Status.SerialNumber}");
            }

            if (details.Status.LifetimePageCount is not null)
            {
                line.Append($"; {details.Status.LifetimePageCount} pages");
            }

            if (details.Status.Detail is not null)
            {
                line.Append($"; {details.Status.Detail}");
            }

            SampleHelpers.AppendMarkers(line, details.Status.Markers);
            return line.ToString();
        }
        catch (Exception exception)
        {
            return $"unavailable: {exception.Message}";
        }
    }
}
