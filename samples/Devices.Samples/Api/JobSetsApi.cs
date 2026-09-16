using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples.Api;

// A job set is the scripted hardware test. The same JSON runs on Windows, Linux and macOS,
// so two machines can be compared job by job.
internal static class JobSetsApi
{
    public static void Map(IEndpointRouteBuilder app)
    {
        _ = app.MapGet("/api/job-sets", async (SamplePaths paths, CancellationToken cancellationToken) =>
            await JobSetReader.ListAsync(paths, cancellationToken).ConfigureAwait(false));

        // The set travels in the body, so an uploaded set and a supplied set use one path.
        // The browser reads the uploaded file itself and sends what it read.
        _ = app.MapPost("/api/job-sets/run", (RunJobSetRequest request, PrintJobRunner runner, HttpContext context) =>
        {
            if (request is null || String.IsNullOrWhiteSpace(request.PrinterId))
            {
                return Results.BadRequest("The request names no printer.");
            }

            var problem = JobSetReader.Validate(request.Set);
            if (problem is not null)
            {
                return Results.BadRequest(problem);
            }

            request.Set.Mode ??= JobSetDto.QueueMode;
            return TypedResults.ServerSentEvents(
                runner.RunSetAsync(PrinterId.ParseOrRaw(request.PrinterId), request.Set, context.RequestAborted),
                "log");
        });
    }
}
