using System.Net;
using System.Text;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reads identity and status details from network printers over IPP (Internet Printing Protocol).
/// Tries IPPS (TLS) first and falls back to plain HTTP across the well-known printer resources.
/// All operations are read-only; nothing is submitted for printing.
/// </summary>
public sealed class IppPrinterStatusClient : IDisposable
{
    /// <summary>
    /// Default IPP port assigned by IANA.
    /// </summary>
    public const int DefaultPort = 631;

    private static readonly string[] Schemes = ["https", "http"];
    private static readonly string[] ResourcePaths = ["/ipp/print", "/ipp/port1"];
    private static readonly string[] RequestedAttributes =
    [
        "printer-state",
        "printer-state-reasons",
        "printer-is-accepting-jobs",
        "printer-make-and-model",
        "marker-names",
        "marker-colors",
        "marker-levels",
    ];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with an
    /// internally managed <see cref="HttpClient"/> that accepts any server certificate,
    /// because network printers overwhelmingly use self-signed certificates.
    /// Supply your own client for custom certificate validation.
    /// </summary>
    public IppPrinterStatusClient()
        : this(CreateDefaultClient(), true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with a
    /// caller-provided <see cref="HttpClient"/>. The client is not disposed by this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    public IppPrinterStatusClient(HttpClient httpClient)
        : this(httpClient, false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    private IppPrinterStatusClient(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    private static HttpClient CreateDefaultClient()
    {
        SocketsHttpHandler handler = new();
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return new HttpClient(handler, true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient && !_disposed)
        {
            _disposed = true;
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Queries identity and status details of a network printer via IPP Get-Printer-Attributes.
    /// </summary>
    /// <param name="host">The printer host name or IP address.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="port">The IPP port. Defaults to 631. Note this is independent of any raw print channel port.</param>
    /// <returns>The printer details.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<IppPrinterDetails> GetDetailsAsync(string host, CancellationToken cancellationToken, int port = DefaultPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        foreach (string scheme in Schemes)
        {
            foreach (string path in ResourcePaths)
            {
                string uri = $"{scheme}://{host}:{port}{path}";
                byte[]? payload = await TryGetAttributesAsync(uri, cancellationToken).ConfigureAwait(false);
                if (payload is null)
                {
                    continue;
                }

                return ParseDetails(host, payload);
            }
        }

        throw new InvalidOperationException($"Printer '{host}:{port}' did not answer IPP status queries over HTTPS or HTTP.");
    }

    private async Task<byte[]?> TryGetAttributesAsync(string uri, CancellationToken cancellationToken)
    {
        using ByteArrayContent content = new(BuildRequest(uri));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/ipp");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException exception)
        {
            if (exception.StatusCode is null)
            {
                return null;
            }

            throw new InvalidOperationException($"IPP query to '{uri}' failed with HTTP {exception.StatusCode:d}.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"IPP query to '{uri}' failed with HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] BuildRequest(string uri)
    {
        using MemoryStream buffer = new();
        buffer.WriteByte(1);
        buffer.WriteByte(1);
        WriteUInt16(buffer, 0x000B);
        WriteInt32(buffer, 1);
        buffer.WriteByte(0x01);
        WriteAttribute(buffer, 0x47, "attributes-charset", "utf-8");
        WriteAttribute(buffer, 0x48, "attributes-natural-language", "en");
        WriteAttribute(buffer, 0x45, "printer-uri", uri);
        WriteAttribute(buffer, 0x44, "requested-attributes", RequestedAttributes[0]);
        for (int i = 1; i < RequestedAttributes.Length; i++)
        {
            WriteValueOnly(buffer, 0x44, RequestedAttributes[i]);
        }

        buffer.WriteByte(0x03);
        return buffer.ToArray();
    }

    private static void WriteUInt16(MemoryStream buffer, int value)
    {
        buffer.WriteByte((byte)(value >> 8));
        buffer.WriteByte((byte)value);
    }

    private static void WriteInt32(MemoryStream buffer, int value)
    {
        buffer.WriteByte((byte)(value >> 24));
        buffer.WriteByte((byte)(value >> 16));
        buffer.WriteByte((byte)(value >> 8));
        buffer.WriteByte((byte)value);
    }

    private static void WriteAttribute(MemoryStream buffer, byte tag, string name, string value)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        buffer.WriteByte(tag);
        WriteUInt16(buffer, nameBytes.Length);
        buffer.Write(nameBytes, 0, nameBytes.Length);
        WriteUInt16(buffer, valueBytes.Length);
        buffer.Write(valueBytes, 0, valueBytes.Length);
    }

    private static void WriteValueOnly(MemoryStream buffer, byte tag, string value)
    {
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        buffer.WriteByte(tag);
        WriteUInt16(buffer, 0);
        WriteUInt16(buffer, valueBytes.Length);
        buffer.Write(valueBytes, 0, valueBytes.Length);
    }

    private static IppPrinterDetails ParseDetails(string host, byte[] payload)
    {
        if (payload.Length < 8)
        {
            throw new InvalidDataException("IPP response is too short.");
        }

        int statusCode = (payload[2] << 8) | payload[3];
        if (statusCode >= 0x0400)
        {
            throw new InvalidOperationException($"Printer reported IPP error status 0x{statusCode:X4}.");
        }

        Dictionary<string, List<IppValue>> attributes = DecodeAttributes(payload);
        PrinterStatusState state = MapState(GetInt(attributes, "printer-state"));
        List<string> reasons = GetStrings(attributes, "printer-state-reasons");
        string? detail = reasons.Count == 0 || (reasons.Count == 1 && reasons[0] == "none")
            ? null
            : string.Join("; ", reasons);
        bool accepting = GetBool(attributes, "printer-is-accepting-jobs") ?? state != PrinterStatusState.Paused;
        string? makeAndModel = GetFirstString(attributes, "printer-make-and-model");

        PrinterId id = PrinterId.FromNetwork(host);
        PrinterStatus status = new(id, state)
        {
            IsAcceptingJobs = accepting,
            Detail = detail,
            Markers = GetMarkers(attributes),
        };
        PrinterInfo info = new(id, makeAndModel ?? host);
        return new IppPrinterDetails(info, status);
    }

    private static Dictionary<string, List<IppValue>> DecodeAttributes(byte[] payload)
    {
        Dictionary<string, List<IppValue>> attributes = new(StringComparer.Ordinal);
        string? currentName = null;
        int position = 8;
        while (position < payload.Length)
        {
            byte tag = payload[position];
            position += 1;
            if (tag == 0x03)
            {
                break;
            }

            if (tag <= 0x0F)
            {
                continue;
            }

            string name = ReadString(payload, ref position);
            byte[] value = ReadBytes(payload, ref position);
            if (name.Length == 0)
            {
                if (currentName is null)
                {
                    throw new InvalidDataException("Malformed IPP response: unnamed first attribute.");
                }

                name = currentName;
            }

            currentName = name;
            if (!attributes.TryGetValue(name, out List<IppValue>? values))
            {
                values = [];
                attributes[name] = values;
            }

            values.Add(new IppValue(tag, value));
        }

        return attributes;
    }

    private static string ReadString(byte[] payload, ref int position) => Encoding.UTF8.GetString(ReadBytes(payload, ref position));

    private static byte[] ReadBytes(byte[] payload, ref int position)
    {
        if (position + 2 > payload.Length)
        {
            throw new InvalidDataException("Malformed IPP response: truncated length.");
        }

        int length = (payload[position] << 8) | payload[position + 1];
        position += 2;
        if (position + length > payload.Length)
        {
            throw new InvalidDataException("Malformed IPP response: truncated value.");
        }

        byte[] value = new byte[length];
        Buffer.BlockCopy(payload, position, value, 0, length);
        position += length;
        return value;
    }

    private static int? GetInt(Dictionary<string, List<IppValue>> attributes, string name)
    {
        if (!attributes.TryGetValue(name, out List<IppValue>? values))
        {
            return null;
        }

        foreach (IppValue value in values)
        {
            if ((value.Tag == 0x21 || value.Tag == 0x23) && value.Value.Length == 4)
            {
                return (value.Value[0] << 24) | (value.Value[1] << 16) | (value.Value[2] << 8) | value.Value[3];
            }
        }

        return null;
    }

    private static bool? GetBool(Dictionary<string, List<IppValue>> attributes, string name)
    {
        if (!attributes.TryGetValue(name, out List<IppValue>? values))
        {
            return null;
        }

        foreach (IppValue value in values)
        {
            if (value.Tag == 0x22 && value.Value.Length == 1)
            {
                return value.Value[0] != 0;
            }
        }

        return null;
    }

    private static List<string> GetStrings(Dictionary<string, List<IppValue>> attributes, string name)
    {
        List<string> result = [];
        if (!attributes.TryGetValue(name, out List<IppValue>? values))
        {
            return result;
        }

        foreach (IppValue value in values)
        {
            result.Add(Encoding.UTF8.GetString(value.Value));
        }

        return result;
    }

    private static string? GetFirstString(Dictionary<string, List<IppValue>> attributes, string name)
    {
        List<string> values = GetStrings(attributes, name);
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<PrinterMarker> GetMarkers(Dictionary<string, List<IppValue>> attributes)
    {
        if (!attributes.TryGetValue("marker-names", out List<IppValue>? names) || names.Count == 0)
        {
            return [];
        }

        attributes.TryGetValue("marker-colors", out List<IppValue>? colors);
        attributes.TryGetValue("marker-levels", out List<IppValue>? levels);

        List<PrinterMarker> markers = new(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            string name = Encoding.UTF8.GetString(names[i].Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            PrinterMarker marker = new(name);
            if (colors is not null && i < colors.Count)
            {
                marker.Color = Encoding.UTF8.GetString(colors[i].Value);
            }

            if (levels is not null && i < levels.Count)
            {
                int? level = GetLevel(levels[i]);
                marker.LevelPercent = level is null || level < 0 ? null : level;
            }

            markers.Add(marker);
        }

        return markers;
    }

    private static int? GetLevel(IppValue value)
    {
        if ((value.Tag == 0x21 || value.Tag == 0x23) && value.Value.Length == 4)
        {
            return (value.Value[0] << 24) | (value.Value[1] << 16) | (value.Value[2] << 8) | value.Value[3];
        }

        return null;
    }

    private static PrinterStatusState MapState(int? state)
    {
        if (state == 3)
        {
            return PrinterStatusState.Idle;
        }

        if (state == 4)
        {
            return PrinterStatusState.Processing;
        }

        if (state == 5)
        {
            return PrinterStatusState.Paused;
        }

        return PrinterStatusState.Unknown;
    }

    private readonly struct IppValue
    {
        public IppValue(byte tag, byte[] value)
        {
            Tag = tag;
            Value = value;
        }

        public byte Tag { get; }

        public byte[] Value { get; }
    }
}
