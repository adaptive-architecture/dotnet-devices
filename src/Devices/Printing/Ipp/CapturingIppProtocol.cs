using SharpIpp.Protocol;

namespace AdaptArch.Devices.Printing.Ipp;

/// <summary>
/// Wraps the IPP protocol reader and keeps the raw response that it read.
/// </summary>
/// <remarks>
/// The strongly-typed model of <c>SharpIppNext</c> does not carry the <c>marker-names</c>,
/// <c>marker-colors</c> and <c>marker-levels</c> attributes, which report the ink and toner
/// levels. Those are read from the raw attributes instead.
/// <para>
/// One instance serves one request, so nothing is shared between callers. Do not reuse an
/// instance for a second request.
/// </para>
/// </remarks>
internal sealed class CapturingIppProtocol : IIppProtocol
{
    private readonly IIppProtocol _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturingIppProtocol"/> class.
    /// </summary>
    /// <param name="inner">The protocol reader that does the work.</param>
    public CapturingIppProtocol(IIppProtocol inner) => _inner = inner;

    /// <summary>
    /// Gets the raw response that was read, or <c>null</c> when none was read.
    /// </summary>
    public IIppResponseMessage? Response { get; private set; }

    /// <inheritdoc />
    public long? MaxDocumentStreamBytes
    {
        get => _inner.MaxDocumentStreamBytes;
        set => _inner.MaxDocumentStreamBytes = value;
    }

    /// <inheritdoc />
    public long? MaxMessageAttributesBytes
    {
        get => _inner.MaxMessageAttributesBytes;
        set => _inner.MaxMessageAttributesBytes = value;
    }

    /// <inheritdoc />
    public int? MaxMessageAttributesCount
    {
        get => _inner.MaxMessageAttributesCount;
        set => _inner.MaxMessageAttributesCount = value;
    }

    /// <inheritdoc />
    public bool ReadDocumentStream
    {
        get => _inner.ReadDocumentStream;
        set => _inner.ReadDocumentStream = value;
    }

    /// <inheritdoc />
    public Task<IIppRequestMessage> ReadIppRequestAsync(Stream stream, CancellationToken cancellationToken = default) =>
        _inner.ReadIppRequestAsync(stream, cancellationToken);

    /// <inheritdoc />
    public async Task<IIppResponseMessage> ReadIppResponseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var message = await _inner.ReadIppResponseAsync(stream, cancellationToken).ConfigureAwait(false);
        Response = message;
        return message;
    }

    /// <inheritdoc />
    public Task WriteIppRequestAsync(IIppRequestMessage ippRequestMessage, Stream stream, CancellationToken cancellationToken = default) =>
        _inner.WriteIppRequestAsync(ippRequestMessage, stream, cancellationToken);

    /// <inheritdoc />
    public Task WriteIppResponseAsync(IIppResponseMessage message, Stream stream, CancellationToken cancellationToken = default) =>
        _inner.WriteIppResponseAsync(message, stream, cancellationToken);
}
