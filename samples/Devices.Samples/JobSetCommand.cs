using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// The job sets without the browser: "job-set <set> <printer-id>" runs one set and writes
// the same lines the page shows, one a line, so a smoke set runs from a script or in CI.
// The set is a file in PrintJobs/, by name with or without ".json", or a path. The exit
// code is 1 when any job failed, and the run goes on past a failure, as the page does.
internal static class JobSetCommand
{
    public const string Verb = "job-set";

    public static bool IsRequested(string[] args) => args.Length > 0 && String.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        if (args.Length != 3)
        {
            await Console.Error.WriteLineAsync($"usage: {Verb} <set-name-or-path> <printer-id>").ConfigureAwait(false);
            return 2;
        }

        using CancellationTokenSource stopping = new();
        Console.CancelKeyPress += (_, pressed) =>
        {
            pressed.Cancel = true;
            stopping.Cancel();
        };

        var set = await JobSetReader.ReadAsync(Resolve(args[1], services.GetRequiredService<SamplePaths>()), stopping.Token).ConfigureAwait(false);
        if (set is null)
        {
            await Console.Error.WriteLineAsync($"'{args[1]}' is not a job set: name one of PrintJobs/ or give a path.").ConfigureAwait(false);
            return 2;
        }

        if (JobSetReader.Validate(set) is string problem)
        {
            await Console.Error.WriteLineAsync(problem).ConfigureAwait(false);
            return 2;
        }

        set.Mode ??= JobSetDto.QueueMode;
        var failed = false;
        await foreach (var line in services.GetRequiredService<PrintJobRunner>().RunSetAsync(PrinterId.ParseOrRaw(args[2]), set, stopping.Token).ConfigureAwait(false))
        {
            failed |= line.Level == LogLineDto.Error;
            Console.WriteLine($"{line.Level}: {line.Text}");
        }

        return failed ? 1 : 0;
    }

    private static string Resolve(string set, SamplePaths paths)
    {
        if (File.Exists(set))
        {
            return set;
        }

        var named = Path.Combine(paths.PrintJobs, set);
        return File.Exists(named) ? named : named + ".json";
    }
}
