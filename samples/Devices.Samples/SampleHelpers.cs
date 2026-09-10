using System.Globalization;
using System.Text;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Samples;

internal static class SampleHelpers
{
    // Printing costs paper and ink, so the sample always asks first.
    internal static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N]: ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    // A warning is yellow, so a skipped job is visible in a long run. The colour is put
    // back whatever happens, or every later line would stay yellow.
    internal static void WriteWarning(string message)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    // Prints a numbered list and reads one number. Returns -1 for a cancel or a bad entry.
    internal static int Choose(string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            Console.WriteLine($"{title}: nothing to choose from.");
            return -1;
        }

        Console.WriteLine(title);
        for (var index = 0; index < items.Count; index++)
        {
            Console.WriteLine($"  {index + 1}) {items[index]}");
        }

        Console.Write("Number (empty to cancel): ");
        var answer = Console.ReadLine();
        if (!Int32.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var choice)
            || choice < 1 || choice > items.Count)
        {
            Console.WriteLine("Cancelled.");
            return -1;
        }

        return choice - 1;
    }

    internal static void AppendMarkers(StringBuilder line, IReadOnlyList<PrinterMarker> markers)
    {
        foreach (var marker in markers)
        {
            line.Append($"; {marker.Name} {(marker.LevelPercent is null ? "level unknown" : marker.LevelPercent + "%")}");
        }
    }

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
    // printer reads. Such a file must not appear in a menu.
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
}
