using System.Text;

namespace AdaptArch.Devices.Printing;

// The text rules of a printer identifier: the percent codec for an authority that holds
// free text, and the split of a network authority into a host and a port.
//
// System.Uri is deliberately not used. It lowercases, normalises and maps international
// names, and it would rewrite a queue name such as "\\server\queue" into something that
// no longer names the queue. Parsing by hand also keeps the code free of a regular
// expression, which keeps the trim and native ahead-of-time analyzers quiet.
internal static class PrinterIdSyntax
{
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>
    /// The characters that end an authority in a URI. One of them inside an identifier is
    /// a parse error, and one of them inside a decoded value is a rejected value.
    /// </summary>
    public static bool IsReservedDelimiter(char value) => value is '/' or '?' or '#';

    public static bool ContainsReservedDelimiter(ReadOnlySpan<char> text)
    {
        foreach (var value in text)
        {
            if (IsReservedDelimiter(value))
            {
                return true;
            }
        }

        return false;
    }

    // RFC 3986 "unreserved". Everything else is escaped, which is one rule with no
    // exceptions to remember.
    private static bool IsUnreserved(char value) =>
        value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';

    /// <summary>
    /// Escapes every character that is not unreserved, over the UTF-8 bytes of the value.
    /// </summary>
    public static string Encode(string value)
    {
        var needed = false;
        foreach (var character in value)
        {
            if (!IsUnreserved(character))
            {
                needed = true;
                break;
            }
        }

        if (!needed)
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        StringBuilder builder = new(bytes.Length * 3);
        foreach (var current in bytes)
        {
            var character = (char)current;
            if (current < 0x80 && IsUnreserved(character))
            {
                _ = builder.Append(character);
            }
            else
            {
                _ = builder.Append('%').Append(HexDigits[current >> 4]).Append(HexDigits[current & 0x0F]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads an escaped value. Decoding is permissive, so a character that an encoder
    /// would have escaped is accepted as itself; only a broken escape is refused.
    /// </summary>
    /// <returns><c>false</c> when an escape is not two hexadecimal digits, when the bytes
    /// are not valid UTF-8, or when the value decodes to a character that would end the
    /// authority.</returns>
    public static bool TryDecode(ReadOnlySpan<char> text, out string value)
    {
        value = String.Empty;
        if (text.IsEmpty)
        {
            return false;
        }

        List<byte> bytes = new(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character != '%')
            {
                // A character above the ASCII range is kept as its own UTF-8 bytes.
                if (character < 0x80)
                {
                    bytes.Add((byte)character);
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
                }

                continue;
            }

            if (i + 2 >= text.Length ||
                !TryReadHexDigit(text[i + 1], out var high) ||
                !TryReadHexDigit(text[i + 2], out var low))
            {
                return false;
            }

            bytes.Add((byte)((high << 4) | low));
            i += 2;
        }

        try
        {
            // A strict decoder refuses a byte sequence that is not valid UTF-8, instead
            // of quietly producing a replacement character.
            value = new UTF8Encoding(false, true).GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (IsReservedDelimiter(character) || Char.IsControl(character))
            {
                value = String.Empty;
                return false;
            }
        }

        return value.Length > 0;
    }

    private static bool TryReadHexDigit(char character, out int digit)
    {
        if (character is >= '0' and <= '9')
        {
            digit = character - '0';
            return true;
        }

        if (character is >= 'a' and <= 'f')
        {
            digit = (character - 'a') + 10;
            return true;
        }

        if (character is >= 'A' and <= 'F')
        {
            digit = (character - 'A') + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    /// <summary>
    /// Splits a network authority into a host and a port. An IPv6 literal is written in
    /// brackets, and the host comes back without them, because
    /// <see cref="NetworkPrinterEndpoint.Host"/> holds the bare address.
    /// </summary>
    /// <returns><c>false</c> when the authority is malformed or the host is not a host.</returns>
    public static bool TrySplitHostPort(ReadOnlySpan<char> authority, out string host, out int port)
    {
        host = String.Empty;
        port = 0;
        if (authority.IsEmpty)
        {
            return false;
        }

        ReadOnlySpan<char> hostText;
        ReadOnlySpan<char> portText = default;
        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            hostText = authority[1..close];
            var rest = authority[(close + 1)..];
            if (!rest.IsEmpty)
            {
                if (rest[0] != ':')
                {
                    return false;
                }

                portText = rest[1..];
            }
        }
        else
        {
            var separator = authority.LastIndexOf(':');
            if (separator >= 0)
            {
                hostText = authority[..separator];
                portText = authority[(separator + 1)..];
            }
            else
            {
                hostText = authority;
            }
        }

        if (hostText.IsEmpty || Uri.CheckHostName(hostText.ToString()) == UriHostNameType.Unknown)
        {
            return false;
        }

        if (!portText.IsEmpty)
        {
            if (!Int32.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                port = 0;
                return false;
            }
        }

        host = hostText.ToString();
        return true;
    }

    /// <summary>
    /// Writes a host back into an authority, adding the brackets an IPv6 literal needs.
    /// </summary>
    public static string FormatHost(string host) =>
        Uri.CheckHostName(host) == UriHostNameType.IPv6 ? String.Concat("[", host, "]") : host;
}
