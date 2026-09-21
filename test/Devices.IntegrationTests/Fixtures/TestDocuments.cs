#nullable enable
using AdaptArch.Devices.Pdf;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// Documents built in code rather than checked in, so what a test sends is readable in the
/// test and nothing depends on a binary file.
/// </summary>
internal static class TestDocuments
{
    /// <summary>
    /// A one-page PDF, the smallest that is still structurally valid: a printer that reads
    /// PDF accepts it, and one that does not has something real to refuse.
    /// </summary>
    public static byte[] OnePagePdf()
    {
        const string Content = "BT /F1 24 Tf 72 700 Td (dotnet-devices integration test) Tj ET";

        return MinimalPdf.From([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            MinimalPdf.Stream(Content),
            MinimalPdf.Helvetica,
        ]);
    }

    /// <summary>A label in ZPL, which every printer language test sends unchanged.</summary>
    public const string ZplLabel = "^XA^FO50,50^ADN,36,20^FDdotnet-devices^FS^XZ";
}
