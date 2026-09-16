using System.Text.Json;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples.Api;

// The two endpoints that print. Both answer with a stream of lines, because a job reports
// its progress over minutes and the person must see it as it happens.
internal static class JobsApi
{
    // Printing costs paper and ink. The browser asks the person before it sends the
    // request, and the server prints only when the request arrives.
    public static void Map(IEndpointRouteBuilder app)
    {
        _ = app.MapPost("/api/jobs", async (
            PrintRequest request,
            PrintJobRunner runner,
            SamplePaths paths,
            HttpContext context) =>
        {
            if (request is null || String.IsNullOrWhiteSpace(request.PrinterId))
            {
                return Results.BadRequest("The request names no printer.");
            }

            var path = SampleHelpers.SafePath(paths.PrintFiles, request.File);
            if (path is null || !File.Exists(path))
            {
                return Results.BadRequest($"'{request.File}' is not in PrintFiles.");
            }

            var bytes = await File.ReadAllBytesAsync(path, context.RequestAborted).ConfigureAwait(false);
            return Start(runner, request.PrinterId, bytes, request.ContentType, Path.GetFileName(path), request.Options, request.Raw, context);
        });

        // The form is read by hand, not bound. One file and a few text fields need no
        // binding, and this keeps the shape the native AOT publish has to reason about
        // as small as possible.
        _ = app.MapPost("/api/jobs/upload", async (HttpContext context, PrintJobRunner runner) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest("The request is not a form.");
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest("The form holds no file.");
            }

            PrintOptionsDto options;
            try
            {
                options = Read(form["options"]);
            }
            catch (JsonException exception)
            {
                return Results.BadRequest($"The options are not readable: {exception.Message}");
            }

            using MemoryStream buffer = new();
            await file.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            return Start(
                runner,
                form["printerId"],
                buffer.ToArray(),
                form["contentType"],
                file.FileName,
                options,
                form["raw"] == "true",
                context);
        });
    }

    private static PrintOptionsDto Read(string text) =>
        String.IsNullOrWhiteSpace(text)
            ? new PrintOptionsDto()
            : JsonSerializer.Deserialize(text, AppJsonContext.Default.PrintOptionsDto) ?? new PrintOptionsDto();

    // The true content type matters. It is what makes the IPP channel negotiate the format,
    // and send a label language as application/vnd.cups-raw instead of as text.
    private static IResult Start(
        PrintJobRunner runner,
        string printerIdText,
        byte[] bytes,
        string contentType,
        string fileName,
        PrintOptionsDto optionsDto,
        bool raw,
        HttpContext context)
    {
        PrinterId printerId;
        string resolved;
        PrintOptions options;
        try
        {
            printerId = PrinterId.ParseOrRaw(printerIdText);
            resolved = String.IsNullOrWhiteSpace(contentType) ? SampleHelpers.GetContentType(fileName) : contentType;
            options = (optionsDto ?? new PrintOptionsDto()).ToOptions(fileName);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            return Results.BadRequest(exception.Message);
        }

        // RequestAborted stops the watch when the page closes, so no poll loop outlives
        // the person who asked for it.
        return TypedResults.ServerSentEvents(
            runner.RunAsync(printerId, bytes, resolved, options, raw, new JobOutcome { What = fileName }, context.RequestAborted),
            "log");
    }
}
