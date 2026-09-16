using System.Text.Json;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// Reads the job sets that ship with the sample. An uploaded set takes the same path, so a
// set that runs here runs on the other operating systems too.
internal static class JobSetReader
{
    // Never throws. A set that cannot be read is left out of the list, because a broken
    // file must not hide the files beside it.
    public static async Task<IReadOnlyList<JobSetDto>> ListAsync(SamplePaths paths, CancellationToken cancellationToken)
    {
        List<JobSetDto> sets = [];
        if (!Directory.Exists(paths.PrintJobs))
        {
            return sets;
        }

        var files = Directory.GetFiles(paths.PrintJobs, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var set = await JsonSerializer.DeserializeAsync(
                    stream, AppJsonContext.Default.JobSetDto, cancellationToken).ConfigureAwait(false);
                if (set is not null)
                {
                    set.Name ??= Path.GetFileNameWithoutExtension(file);
                    sets.Add(set);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                // The file is not a job set. The list is still useful without it.
            }
        }

        return sets;
    }

    // Returns the reason the set cannot run, or null when it can. The caller answers 400
    // with this text, so a wrong set says what is wrong with it.
    public static string Validate(JobSetDto set)
    {
        if (set is null)
        {
            return "The request holds no job set.";
        }

        if (set.Jobs is null || set.Jobs.Count == 0)
        {
            return "The job set holds no job.";
        }

        var mode = set.Mode ?? JobSetDto.QueueMode;
        if (mode != JobSetDto.QueueMode && mode != JobSetDto.RawMode)
        {
            return $"'{mode}' is not a mode. Write '{JobSetDto.QueueMode}' or '{JobSetDto.RawMode}'.";
        }

        foreach (var job in set.Jobs)
        {
            if (String.IsNullOrWhiteSpace(job.File))
            {
                return "A job in the set names no file.";
            }
        }

        return null;
    }
}
