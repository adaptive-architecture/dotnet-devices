#nullable enable
using System.Text;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for a real renderer. It emits a sentinel instead of pages, so a test can prove
/// the converted bytes — and not the original document — are what reached the printer, and
/// can read back what the library asked the converter to produce.
/// </summary>
internal sealed class RecordingPayloadConverter : IPrintPayloadConverter
{
    public const string Sentinel = "converted-by-the-test";

    private readonly string _reads;
    private readonly string[] _emits;

    public RecordingPayloadConverter(string reads, params string[] emits)
    {
        _reads = reads;
        _emits = emits;
    }

    public PrintConversionContext? LastContext { get; private set; }

    public byte[]? LastInput { get; private set; }

    public int Calls { get; private set; }

    public bool CanConvert(string contentType) =>
        String.Equals(contentType, _reads, StringComparison.OrdinalIgnoreCase);

    public bool CanEmit(string targetContentType) =>
        _emits.Contains(targetContentType, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        Calls++;
        LastInput = data;
        LastContext = context;

        // One document, not one page: every target the library negotiates carries the whole
        // job in a single stream, and it rejects any other answer.
        IReadOnlyList<byte[]> documents = [Encoding.UTF8.GetBytes($"{Sentinel}:{context.TargetContentType}:{context.Dpi}")];
        return Task.FromResult(documents);
    }
}
