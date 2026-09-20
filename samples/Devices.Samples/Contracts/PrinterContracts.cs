namespace AdaptArch.Devices.Samples.Contracts;

// One physical printer, as the browser needs it. The device carries the identity and the
// command sets; a channel carries the identifier, the capabilities and the promises.
internal sealed record DeviceDto(
    string Id,
    string Key,
    string Name,
    string Summary,
    string Manufacturer,
    string Model,
    string SerialNumber,
    string Location,
    IReadOnlyList<string> ContributedBy,
    IReadOnlyList<string> StatusSources,
    IReadOnlyList<string> CommandSets,
    IReadOnlyList<ChannelDto> Channels);

// One way to reach that printer. "Accepts" is the tri-state answer of
// PrinterDevice.Accepts for the content type the browser asked about, so the page can warn
// before it wastes paper.
internal sealed record ChannelDto(
    string Id,
    string Scheme,
    string Address,
    string Summary,
    bool HasJobQueue,
    bool GivesPassthrough,
    string Reads,
    ConfigurationDto Configuration);

// What the printer reported. An empty list means it reported nothing, which is not a
// refusal. A null in a tri-state means the same.
internal sealed record ConfigurationDto(
    IReadOnlyList<string> DocumentFormats,
    IReadOnlyList<string> Orientations,
    IReadOnlyList<string> Scalings,
    IReadOnlyList<string> Media,
    IReadOnlyList<string> MediaSources,
    IReadOnlyList<string> MediaTypes,
    IReadOnlyList<string> OutputBins,
    IReadOnlyList<string> Qualities,
    IReadOnlyList<int> ResolutionsDpi,
    IReadOnlyList<int> NumberUpValues,
    bool? SupportsDuplex,
    bool? SupportsColor,
    bool? SupportsPageRanges,
    string DefaultMediaSize,
    string DefaultMediaSource,
    string DefaultOrientation,
    int? DefaultResolutionDpi);

internal sealed record StatusDto(
    string State,
    bool IsAcceptingJobs,
    string Detail,
    string SerialNumber,
    long? LifetimePageCount,
    IReadOnlyList<string> Markers);

// A file in PrintFiles. A file no printer reads, such as a GIMP .xcf, has CanPrint false
// and stays out of the selector.
internal sealed record FileDto(string Name, string ContentType, bool CanPrint, long Size);

// The answer of PrinterDevice.Accepts for one channel and one content type. A null answer
// is not a refusal: the channel reported nothing. "Reads" is the list the channel did
// report, which is the useful next step when the answer is false.
internal sealed record AcceptsDto(bool? Accepts, string Reads);

// One PDF rendering engine the process registered. The name is what a job puts in
// PrintOptions.ConverterName; IsDefault marks the one a job that names none will get, which
// is simply the first the policy lists.
internal sealed record EngineDto(string Name, bool IsDefault);
