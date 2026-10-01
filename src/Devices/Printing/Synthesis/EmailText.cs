using System.Text;
using System.Text.RegularExpressions;

namespace AdaptArch.Devices.Printing.Synthesis;

// Reads an RFC 5322 message as the text a person reads: the From, To, Cc, Date and Subject
// headers, then the first plain-text part that is not an attachment. HTML is never rendered,
// so a message with no plain-text part is refused.
internal static partial class EmailText
{
    private static readonly string[] PrintedHeaders = ["From", "To", "Cc", "Date", "Subject"];

    static EmailText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static EmailRendering Render(ReadOnlySpan<byte> message)
    {
        var entity = Entity.Parse(Encoding.Latin1.GetString(message));
        var skipped = 0;
        var body = FindText(entity, ref skipped)
            ?? throw new NotSupportedException(
                "The email has no text/plain part, and the library prints no HTML: send a message with a plain-text part, or convert it first.");

        StringBuilder text = new();
        foreach (var name in PrintedHeaders)
        {
            if (entity.Header(name) is string value)
            {
                _ = text.Append(name).Append(": ").AppendLine(DecodeWords(value));
            }
        }

        _ = text.AppendLine().Append(body);
        return new EmailRendering(text.ToString(), skipped);
    }

    private static string? FindText(Entity entity, ref int skipped)
    {
        if (entity.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            // The parts of an alternative say the same thing, so the ones not printed lose nothing.
            var alternative = String.Equals(entity.MediaType, "multipart/alternative", StringComparison.OrdinalIgnoreCase);
            string? found = null;
            foreach (var part in entity.Parts())
            {
                if (found is not null)
                {
                    skipped += alternative ? 0 : 1;
                    continue;
                }

                var before = skipped;
                found = FindText(part, ref skipped);
                skipped = alternative ? before : skipped;
            }

            return found;
        }

        if (String.Equals(entity.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase) && !entity.IsAttachment)
        {
            return entity.Text();
        }

        skipped++;
        return null;
    }

    // RFC 2047: =?charset?B|Q?text?=, with the space between two adjacent words dropped.
    internal static string DecodeWords(string value)
    {
        var joined = AdjacentWords().Replace(value, "$1");
        return EncodedWord().Replace(joined, match =>
        {
            var encoding = EncodingFor(match.Groups["charset"].Value);
            var data = match.Groups["data"].Value;
            var bytes = match.Groups["mode"].Value is "B" or "b"
                ? Convert.FromBase64String(data)
                : QuotedPrintable(data.Replace('_', ' '), header: true);
            return encoding.GetString(bytes);
        });
    }

    internal static byte[] QuotedPrintable(string text, bool header = false)
    {
        using MemoryStream bytes = new();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '=' && index + 2 < text.Length && Uri.IsHexDigit(text[index + 1]) && Uri.IsHexDigit(text[index + 2]))
            {
                bytes.WriteByte(Convert.ToByte(text.Substring(index + 1, 2), 16));
                index += 2;
            }
            else if (character == '=' && !header && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }
            else if (character == '=' && !header && index == text.Length - 1)
            {
                break;
            }
            else
            {
                bytes.WriteByte((byte)character);
            }
        }

        return bytes.ToArray();
    }

    internal static Encoding EncodingFor(string? charset)
    {
        if (String.IsNullOrWhiteSpace(charset))
        {
            return Encoding.Latin1;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    [GeneratedRegex(@"(=\?[^?\s]+\?[BbQq]\?[^?\s]*\?=)\s+(?==\?)")]
    private static partial Regex AdjacentWords();

    [GeneratedRegex(@"=\?(?<charset>[^?\s*]+)(\*[^?\s]*)?\?(?<mode>[BbQq])\?(?<data>[^?\s]*)\?=")]
    private static partial Regex EncodedWord();

    // One MIME entity: its headers, unfolded, and its body as the octets it was sent with,
    // held one per character.
    private sealed class Entity
    {
        private readonly List<(string Name, string Value)> _headers;
        private readonly string _body;

        private Entity(List<(string Name, string Value)> headers, string body)
        {
            _headers = headers;
            _body = body;
            var contentType = Parameters(Header("Content-Type") ?? "text/plain");
            MediaType = contentType.Value.ToLowerInvariant();
            ContentParameters = contentType.Parameters;
        }

        public string MediaType { get; }

        public bool IsAttachment =>
            Header("Content-Disposition")?.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase) == true;

        private Dictionary<string, string> ContentParameters { get; }

        public static Entity Parse(string text)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            var split = normalized.StartsWith('\n') ? 0 : normalized.IndexOf("\n\n", StringComparison.Ordinal);
            var head = split < 0 ? normalized : normalized[..split];
            var body = split < 0 ? String.Empty : normalized[(split + (split == 0 ? 1 : 2))..];

            List<(string Name, string Value)> headers = [];
            foreach (var line in head.Split('\n'))
            {
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && headers.Count > 0)
                {
                    headers[^1] = (headers[^1].Name, headers[^1].Value + " " + line.Trim());
                }
                else if (line.IndexOf(':', StringComparison.Ordinal) is var colon and > 0)
                {
                    headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
                }
            }

            return new Entity(headers, body);
        }

        public string? Header(string name) =>
            _headers.FirstOrDefault(header => String.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        public IEnumerable<Entity> Parts()
        {
            if (!ContentParameters.TryGetValue("boundary", out var boundary) || boundary.Length == 0)
            {
                yield break;
            }

            var delimiter = "--" + boundary;
            StringBuilder? part = null;
            foreach (var line in _body.Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed == delimiter + "--")
                {
                    break;
                }

                if (trimmed == delimiter)
                {
                    if (part is not null)
                    {
                        yield return Parse(part.ToString());
                    }

                    part = new StringBuilder();
                }
                else
                {
                    _ = part?.Append(line).Append('\n');
                }
            }

            if (part is not null)
            {
                yield return Parse(part.ToString());
            }
        }

        public string Text()
        {
            var transfer = Header("Content-Transfer-Encoding")?.Trim().ToLowerInvariant();
            byte[] bytes;
            if (transfer == "base64")
            {
                bytes = Convert.FromBase64String(new string([.. _body.Where(static character => !Char.IsWhiteSpace(character))]));
            }
            else if (transfer == "quoted-printable")
            {
                bytes = QuotedPrintable(_body);
            }
            else
            {
                bytes = Encoding.Latin1.GetBytes(_body);
            }

            var charset = ContentParameters.GetValueOrDefault("charset");
            var text = (charset is null && IsUtf8(bytes) ? Encoding.UTF8 : EncodingFor(charset ?? "us-ascii")).GetString(bytes);
            return String.Equals(ContentParameters.GetValueOrDefault("format"), "flowed", StringComparison.OrdinalIgnoreCase)
                ? Unflow(text, String.Equals(ContentParameters.GetValueOrDefault("delsp"), "yes", StringComparison.OrdinalIgnoreCase))
                : text;
        }

        private static bool IsUtf8(byte[] bytes)
        {
            try
            {
                _ = new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        // RFC 3676: a line that ends in a space continues on the next, except the signature
        // separator; a leading space was added to protect the line and is removed.
        private static string Unflow(string text, bool deleteSpace)
        {
            StringBuilder joined = new();
            foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = raw.StartsWith(' ') ? raw[1..] : raw;
                if (line.EndsWith(' ') && line != "-- ")
                {
                    _ = joined.Append(deleteSpace ? line[..^1] : line);
                }
                else
                {
                    _ = joined.Append(line).Append('\n');
                }
            }

            return joined.ToString();
        }

        private static (string Value, Dictionary<string, string> Parameters) Parameters(string header)
        {
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            var parts = ParameterSplit().Split(header);
            for (var index = 1; index < parts.Length; index++)
            {
                var equals = parts[index].IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                {
                    parameters[parts[index][..equals].Trim()] = parts[index][(equals + 1)..].Trim().Trim('"');
                }
            }

            return (parts[0].Trim(), parameters);
        }
    }

    // Semicolons outside quotes.
    [GeneratedRegex(";(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)")]
    private static partial Regex ParameterSplit();
}

// The text to print, and how many parts of the message were not printed.
internal sealed record EmailRendering(string Text, int SkippedParts);
