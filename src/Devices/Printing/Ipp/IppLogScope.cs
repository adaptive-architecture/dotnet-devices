using System.Collections;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Ipp;

// The scope one IPP operation of one printer runs in, so the wire events of two concurrent
// jobs to one endpoint can be told apart. A structured provider reads the pairs as
// properties; a plain one prints ToString(). The correlation identifier is what ties the
// lines of one operation together when the job has no identifier yet.
internal sealed class IppLogScope : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _values;

    private IppLogScope(PrinterId printerId, string operation, string? jobId)
    {
        List<KeyValuePair<string, object?>> values =
        [
            new("PrinterId", printerId),
            new("IppOperation", operation),
            new("IppCorrelationId", Guid.NewGuid().ToString("n")),
        ];
        if (jobId is not null)
        {
            values.Add(new("JobId", jobId));
        }

        _values = [.. values];
    }

    public int Count => _values.Length;

    public KeyValuePair<string, object?> this[int index] => _values[index];

    public static IDisposable? Begin(ILogger logger, PrinterId printerId, string operation, string? jobId = null) =>
        logger.BeginScope(new IppLogScope(printerId, operation, jobId));

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => String.Join(' ', _values.Select(static pair => $"{pair.Key}={pair.Value}"));
}
