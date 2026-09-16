namespace AdaptArch.Devices.Samples;

// The two content folders the sample ships. They sit beside the program, so a published
// binary finds them wherever it was copied to.
internal sealed class SamplePaths
{
    public SamplePaths()
        : this(AppContext.BaseDirectory)
    {
    }

    public SamplePaths(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        PrintFiles = Path.Combine(baseDirectory, "PrintFiles");
        PrintJobs = Path.Combine(baseDirectory, "PrintJobs");
    }

    public string PrintFiles { get; }

    public string PrintJobs { get; }
}
