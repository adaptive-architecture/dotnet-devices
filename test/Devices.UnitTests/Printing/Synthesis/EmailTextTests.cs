using System.Text;
using AdaptArch.Devices.Printing.Synthesis;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Synthesis;

public class EmailTextTests
{
    [Fact]
    public void Render_ASinglePartMessage_PrintsTheHeadersThenTheBody()
    {
        var rendering = Render(
            "From: Ann <ann@example.com>\r\nTo: Bob <bob@example.com>\r\nSubject: Hello\r\nX-Mailer: test\r\n\r\nThe body.\r\n");

        Assert.Equal("From: Ann <ann@example.com>\nTo: Bob <bob@example.com>\nSubject: Hello\n\nThe body.\n", Lf(rendering.Text));
        Assert.Equal(0, rendering.SkippedParts);
    }

    [Fact]
    public void Render_FoldedAndEncodedHeaders_AreUnfoldedAndDecoded()
    {
        var rendering = Render(
            "Subject: =?utf-8?B?Q2Fmw6k=?= =?iso-8859-1?Q?_au_lait?=\r\n and more\r\n\r\nx");

        Assert.StartsWith("Subject: Café au lait and more", rendering.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AnAlternative_PrintsThePlainTextAndSkipsNothing()
    {
        var rendering = Render("""
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/plain; charset=utf-8
            Content-Transfer-Encoding: quoted-printable

            Caf=C3=A9 =
            ok
            --b1
            Content-Type: text/html

            <p>Café</p>
            --b1--
            """);

        Assert.EndsWith("Café ok\n", Lf(rendering.Text), StringComparison.Ordinal);
        Assert.Equal(0, rendering.SkippedParts);
    }

    [Fact]
    public void Render_AMixedMessage_CountsTheAttachmentsItSkips()
    {
        var rendering = Render("""
            Content-Type: multipart/mixed; boundary=outer

            --outer
            Content-Type: multipart/alternative; boundary=inner

            --inner
            Content-Type: text/plain
            Content-Transfer-Encoding: base64

            SGVsbG8=
            --inner
            Content-Type: text/html

            <b>Hello</b>
            --inner--
            --outer
            Content-Type: application/pdf
            Content-Disposition: attachment; filename="a.pdf"

            JVBERg==
            --outer
            Content-Type: text/plain
            Content-Disposition: attachment; filename="notes.txt"

            notes
            --outer--
            """);

        Assert.EndsWith("\nHello", rendering.Text, StringComparison.Ordinal);
        Assert.Equal(2, rendering.SkippedParts);
    }

    [Fact]
    public void Render_FlowedText_JoinsTheSoftBreaks()
    {
        var rendering = Render("Content-Type: text/plain; format=flowed\r\n\r\nOne long \r\nline.\r\n-- \r\nSig\r\n");

        Assert.EndsWith("One long line.\n-- \nSig\n\n", Lf(rendering.Text), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_HtmlOnly_IsRefused()
    {
        var error = Assert.Throws<NotSupportedException>(() => Render("Content-Type: text/html\r\n\r\n<p>Hi</p>"));

        Assert.Contains("no text/plain part", error.Message, StringComparison.Ordinal);
    }

    private static EmailRendering Render(string message) => EmailText.Render(Encoding.UTF8.GetBytes(message));

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
