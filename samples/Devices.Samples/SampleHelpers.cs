using System.Globalization;
using System.Text;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Samples;

internal static class SampleHelpers
{
    internal static IReadOnlyList<string> GetPrintFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<string> files = [];
        foreach (var path in Directory.GetFiles(directory))
        {
            files.Add(Path.GetFileName(path));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    internal static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".zpl")
        {
            return PrinterContentTypes.Zpl;
        }

        if (extension == ".epl")
        {
            return PrinterContentTypes.Epl;
        }

        if (extension == ".png")
        {
            return PrinterContentTypes.Png;
        }

        if (extension is ".jpg" or ".jpeg")
        {
            return PrinterContentTypes.Jpeg;
        }

        if (extension == ".pdf")
        {
            return PrinterContentTypes.Pdf;
        }

        throw new NotSupportedException($"Files with extension '{extension}' are not supported.");
    }

    // The print files directory can hold a working file, such as a GIMP .xcf, that no
    // printer reads. Such a file must not appear in a selector.
    internal static bool CanPrint(string fileName)
    {
        try
        {
            _ = GetContentType(fileName);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    // A colour choice only means something for an image the printer renders.
    internal static bool IsImage(string contentType) =>
        contentType == PrinterContentTypes.Png || contentType == PrinterContentTypes.Jpeg;

    internal static string DescribeJobReading(PrintJobInfo reading)
    {
        StringBuilder line = new(reading.State.ToString());
        if (reading.ImpressionsCompleted is not null || reading.TotalImpressions is not null)
        {
            var total = reading.TotalImpressions is int totalImpressions ? totalImpressions.ToString(CultureInfo.InvariantCulture) : "?";
            line.Append($" {reading.ImpressionsCompleted ?? 0}/{total} pages");
        }

        if (reading.Detail is not null)
        {
            line.Append($"; {reading.Detail}");
        }

        return line.ToString();
    }

    internal static void AppendMarkers(StringBuilder line, IReadOnlyList<PrinterMarker> markers)
    {
        foreach (var marker in markers)
        {
            line.Append($"; {marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
        }
    }

    // A file name that comes from the browser must not reach outside the directory.
    internal static string SafePath(string directory, string fileName)
    {
        var safe = Path.GetFileName(fileName ?? String.Empty);
        return safe.Length == 0 ? null : Path.Combine(directory, safe);
    }
}
