using System.Globalization;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Samples.Contracts;

// The options of one job, as the browser sends them and as a job set JSON holds them.
// PrintOptions itself is not used here: its setters throw, so a bad number would surface
// as an exception from a property and not as a 400 with a message.
internal sealed class PrintOptionsDto
{
    public int? Copies { get; set; }

    public string Duplex { get; set; }

    public string ColorMode { get; set; }

    public string Orientation { get; set; }

    public string Scaling { get; set; }

    public string MediaSource { get; set; }

    public string MediaSize { get; set; }

    public string MediaType { get; set; }

    public string OutputBin { get; set; }

    public int? ResolutionDpi { get; set; }

    public string Quality { get; set; }

    // "1-3,5" reads better in a JSON file and in a form field than a list of objects.
    public string PageRanges { get; set; }

    public int? NumberUp { get; set; }

    public string JobName { get; set; }

    // Throws FormatException with the name of the property that is wrong. The caller turns
    // that into a 400, so a typo in an uploaded job set names itself.
    public PrintOptions ToOptions(string jobName)
    {
        PrintOptions options = new()
        {
            Copies = Positive(Copies, nameof(Copies)),
            Duplex = ParseEnum<DuplexMode>(Duplex, nameof(Duplex)),
            ColorMode = ParseEnum<PrintColorMode>(ColorMode, nameof(ColorMode)),
            Orientation = ParseEnum<PrintOrientation>(Orientation, nameof(Orientation)),
            Scaling = ParseEnum<PrintScaling>(Scaling, nameof(Scaling)),
            Quality = ParseEnum<PrintQuality>(Quality, nameof(Quality)),
            MediaSource = Trim(MediaSource),
            MediaSize = Trim(MediaSize),
            MediaType = Trim(MediaType),
            OutputBin = Trim(OutputBin),
            ResolutionDpi = ResolutionDpi,
            NumberUp = Positive(NumberUp, nameof(NumberUp)),
            PageRanges = ParseRanges(PageRanges),
            JobName = Trim(JobName) ?? jobName,
        };

        return options;
    }

    private static string Trim(string value) => String.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // PrintOptions.Copies and NumberUp throw ArgumentOutOfRangeException for zero. The
    // check happens here instead, so the message names the property.
    private static int? Positive(int? value, string name)
    {
        if (value is int number && number < 1)
        {
            throw new FormatException($"'{name}' must be 1 or more, but it is {number}.");
        }

        return value;
    }

    private static T? ParseEnum<T>(string text, string name)
        where T : struct
    {
        var trimmed = Trim(text);
        if (trimmed is null)
        {
            return null;
        }

        if (!Enum.TryParse<T>(trimmed, ignoreCase: true, out var value))
        {
            throw new FormatException($"'{trimmed}' is not a value of '{name}'.");
        }

        return value;
    }

    // "1-3,5" becomes two ranges. A single page is a range of one page.
    private static IReadOnlyList<PageRange> ParseRanges(string text)
    {
        var trimmed = Trim(text);
        if (trimmed is null)
        {
            return null;
        }

        List<PageRange> ranges = [];
        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('-', StringComparison.Ordinal);
            var from = separator < 0 ? part : part[..separator];
            var to = separator < 0 ? part : part[(separator + 1)..];
            if (!Int32.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
                || !Int32.TryParse(to, NumberStyles.Integer, CultureInfo.InvariantCulture, out var last))
            {
                throw new FormatException($"'{part}' is not a page range. Write '1-3' or '5', and separate ranges with a comma.");
            }

            ranges.Add(new PageRange(first, last));
        }

        return ranges;
    }
}

// What the browser sends to print one file that is already on the machine.
internal sealed class PrintRequest
{
    public string PrinterId { get; set; }

    public string File { get; set; }

    public string ContentType { get; set; }

    // True sends the bytes unchanged. The printer must read the format itself.
    public bool Raw { get; set; }

    public PrintOptionsDto Options { get; set; }
}

internal sealed record JobDto(string JobId, string State, string Detail, IReadOnlyList<string> DroppedOptions);

// One line of a live stream. The level colours the line; it does not change what happened.
internal sealed record LogLineDto(string Text, string Level)
{
    internal const string Info = "info";
    internal const string Warning = "warning";
    internal const string Error = "error";
    internal const string Done = "done";

    internal static LogLineDto Say(string text) => new(text, Info);

    internal static LogLineDto Warn(string text) => new(text, Warning);

    internal static LogLineDto Fail(string text) => new(text, Error);
}

// A job set, as PrintJobs/*.json holds it and as an upload sends it.
internal sealed class JobSetDto
{
    internal const string QueueMode = "queue";
    internal const string RawMode = "raw";

    public string Name { get; set; }

    public string Description { get; set; }

    // "queue" converts the job on the way; "raw" sends the bytes unchanged.
    public string Mode { get; set; }

    public List<JobDefinitionDto> Jobs { get; set; }
}

internal sealed class JobDefinitionDto
{
    public string File { get; set; }

    public string Description { get; set; }

    // Without this, the file extension decides.
    public string ContentType { get; set; }

    public PrintOptionsDto Options { get; set; }
}

// The set travels in the body, so an uploaded set and a supplied set use one path.
internal sealed class RunJobSetRequest
{
    public string PrinterId { get; set; }

    public JobSetDto Set { get; set; }
}
