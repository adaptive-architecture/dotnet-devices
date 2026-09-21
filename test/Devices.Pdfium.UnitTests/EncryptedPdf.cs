#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// A password-protected PDF, built in code like every other fixture here.
/// </summary>
/// <remarks>
/// The standard security handler at revision 2, which is ISO 32000-1 algorithms 2, 3, 4 and
/// 5: a 40-bit RC4 key derived from the password, and one key for each object derived from
/// that. It is the oldest and weakest of them, which is exactly why it is the one to write
/// here — every engine still opens it, and the whole handler is the sixty lines below rather
/// than a checked-in binary nobody can read.
/// <para>
/// The test that matters is that the engine opens this with the password and refuses it
/// without, so a mistake in the algorithm shows up as a failing test and not as a false pass.
/// </para>
/// </remarks>
internal static class EncryptedPdf
{
    /// <summary>The 32-octet padding string of ISO 32000-1, table 3.4.</summary>
    private static readonly byte[] Pad =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56,
        0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    // The file identifier. Any value does, as long as the same one goes into the key and into
    // the trailer, so a fixed one keeps the fixture reproducible.
    private static readonly byte[] Id = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

    // Every permission granted. It travels into the key, so the two have to agree.
    private const int Permissions = -1;

    private const int KeyLength = 5;

    /// <summary>
    /// One page of one inch square, filled with black, that opens only with the password.
    /// </summary>
    /// <param name="userPassword">The password a reader has to give.</param>
    /// <returns>The document.</returns>
    internal static byte[] OnePage(string userPassword)
    {
        var owner = Owner(userPassword);
        var key = Key(userPassword, owner);
        var user = Rc4(key, Pad);

        const string content = "0 0 0 rg 0 0 72 72 re f";
        var encrypted = Rc4(ObjectKey(key, 4), Encoding.ASCII.GetBytes(content));

        List<byte> body = [];
        List<int> offsets = [];

        void Add(string text)
        {
            offsets.Add(body.Count);
            body.AddRange(Encoding.Latin1.GetBytes(text));
        }

        body.AddRange(Encoding.Latin1.GetBytes("%PDF-1.4\n"));
        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Add("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 4 0 R >>\nendobj\n");

        // The stream is encrypted with the key of object 4. Its length is the length of the
        // cipher text, which RC4 leaves equal to the plain text.
        offsets.Add(body.Count);
        body.AddRange(Encoding.Latin1.GetBytes($"4 0 obj\n<< /Length {encrypted.Length} >>\nstream\n"));
        body.AddRange(encrypted);
        body.AddRange(Encoding.Latin1.GetBytes("\nendstream\nendobj\n"));

        // The encryption dictionary is never itself encrypted, and neither is the identifier.
        Add($"5 0 obj\n<< /Filter /Standard /V 1 /R 2 /O <{Hex(owner)}> /U <{Hex(user)}> /P {Permissions} >>\nendobj\n");

        var startXref = body.Count;
        StringBuilder tail = new();
        _ = tail.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        _ = tail.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            _ = tail.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        _ = tail.Append("trailer\n<< /Size ").Append(offsets.Count + 1)
            .Append(" /Root 1 0 R /Encrypt 5 0 R /ID [<").Append(Hex(Id)).Append("> <").Append(Hex(Id))
            .Append(">] >>\nstartxref\n").Append(startXref).Append("\n%%EOF\n");
        body.AddRange(Encoding.Latin1.GetBytes(tail.ToString()));

        return [.. body];
    }

    // Algorithm 3. The owner password is the user password here, which is what a document
    // with only a user password does.
    private static byte[] Owner(string userPassword)
    {
        var digest = MD5.HashData(Padded(userPassword));
        return Rc4(digest[..KeyLength], Padded(userPassword));
    }

    // Algorithm 2: the password, the owner entry, the permissions in four little-endian
    // octets, and the first identifier, hashed down to the key length.
    private static byte[] Key(string userPassword, byte[] owner)
    {
        List<byte> input = [.. Padded(userPassword), .. owner];
        input.AddRange(BitConverter.GetBytes(Permissions));
        input.AddRange(Id);
        return MD5.HashData([.. input])[..KeyLength];
    }

    // Algorithm 1: the key of one object is the file key, its number and its generation,
    // hashed. Revision 2 always uses five octets, so the object key is ten.
    private static byte[] ObjectKey(byte[] key, int objectNumber)
    {
        List<byte> input =
        [
            .. key,
            (byte)(objectNumber & 0xFF),
            (byte)((objectNumber >> 8) & 0xFF),
            (byte)((objectNumber >> 16) & 0xFF),
            0,
            0,
        ];

        return MD5.HashData([.. input])[..Math.Min(KeyLength + 5, 16)];
    }

    // The password padded to 32 octets, truncated first when it is longer.
    private static byte[] Padded(string password)
    {
        var bytes = Encoding.ASCII.GetBytes(password);
        var padded = new byte[32];
        var taken = Math.Min(bytes.Length, 32);
        Array.Copy(bytes, padded, taken);
        Array.Copy(Pad, 0, padded, taken, 32 - taken);
        return padded;
    }

    // RC4, which .NET does not carry and which is fifteen lines. Deliberately here and not in
    // src: nothing this library ships encrypts anything.
    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            state[i] = (byte)i;
        }

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        var x = 0;
        var y = 0;
        for (var index = 0; index < data.Length; index++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[index] = (byte)(data[index] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        return output;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
