using System.Text.Json.Serialization;

namespace AdaptArch.Devices.Samples.Contracts;

// Every type that crosses the wire is listed here. The native AOT publish has no
// reflection to fall back on, so a type that is missing fails at run time and not at build
// time. Add a contract to this list in the same change that adds the contract.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IReadOnlyList<DeviceDto>))]
[JsonSerializable(typeof(IReadOnlyList<EngineDto>))]
[JsonSerializable(typeof(IReadOnlyList<FileDto>))]
[JsonSerializable(typeof(IReadOnlyList<JobSetDto>))]
[JsonSerializable(typeof(StatusDto))]
[JsonSerializable(typeof(AcceptsDto))]
[JsonSerializable(typeof(JobDto))]
[JsonSerializable(typeof(LogLineDto))]
[JsonSerializable(typeof(PrintRequest))]
[JsonSerializable(typeof(PrintOptionsDto))]
[JsonSerializable(typeof(RunJobSetRequest))]
[JsonSerializable(typeof(JobSetDto))]
[JsonSerializable(typeof(CorrelateRequest))]
[JsonSerializable(typeof(WindowsSpoolerRequest))]
[JsonSerializable(typeof(HostDetailsDto))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
