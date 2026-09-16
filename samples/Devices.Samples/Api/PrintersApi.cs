using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples.Api;

// Discovery, status and the files the sample ships. Nothing here prints.
internal static class PrintersApi
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // A printer identifier is a URI, so it travels in the query and not in the path.
        // A path segment that holds an encoded slash is refused by many front ends.
        _ = app.MapGet("/api/printers", async (
            PrinterCatalog catalog,
            bool? refresh,
            bool? probe,
            CancellationToken cancellationToken) =>
        {
            var devices = await catalog.GetAsync(refresh ?? false, probe ?? false, cancellationToken).ConfigureAwait(false);
            return PrinterProjection.Devices(devices);
        });

        _ = app.MapGet("/api/printers/status", async (
            string id,
            IPrinterManager manager,
            CancellationToken cancellationToken) =>
        {
            if (!PrinterId.TryParse(id, out var printerId) && !TryRaw(id, out printerId))
            {
                return Results.BadRequest($"'{id}' is not a printer identifier.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var status = await manager.GetStatusAsync(printerId, timeoutSource.Token).ConfigureAwait(false);
                return Results.Ok(PrinterProjection.Status(status));
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or OperationCanceledException)
            {
                // A printer that cannot report status is not an error of this application.
                return Results.Problem($"No status: {exception.Message}", statusCode: 502);
            }
        });

        // The tri-state answer, before the person presses Print. A null answer is not a
        // refusal: the channel reported nothing.
        _ = app.MapGet("/api/printers/accepts", (string id, string contentType, PrinterCatalog catalog) =>
            catalog.Accepts(PrinterId.ParseOrRaw(id), contentType));

        _ = app.MapGet("/api/files", (SamplePaths paths) =>
        {
            List<FileDto> files = [];
            foreach (var name in SampleHelpers.GetPrintFiles(paths.PrintFiles))
            {
                var canPrint = SampleHelpers.CanPrint(name);
                var size = new FileInfo(Path.Combine(paths.PrintFiles, name)).Length;
                files.Add(new FileDto(name, canPrint ? SampleHelpers.GetContentType(name) : null, canPrint, size));
            }

            return (IReadOnlyList<FileDto>)files;
        });
    }

    // A value that is not a URI is read as a raw network address, so a plain IP address
    // still works, exactly as it did on the command line.
    private static bool TryRaw(string text, out PrinterId id)
    {
        try
        {
            id = PrinterId.ParseOrRaw(text);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            id = default;
            return false;
        }
    }
}
