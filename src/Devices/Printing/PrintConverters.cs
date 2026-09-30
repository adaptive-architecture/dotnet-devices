namespace AdaptArch.Devices.Printing;

// The answer a job gets when it names a converter no registration carries, or one the
// channel cannot run.
//
// Both the IPP path and the Windows spooler path can be asked for one by name, and a caller
// that mistyped it should read the same sentence whichever channel it reached. Naming what is
// registered matters more than usual here: the answer depends on which packages the process
// referenced, which is not something the caller can read off its own code.
internal static class PrintConverters
{
    // The converter a job asked for: the one it names, or else the one its format requires.
    internal static string? NameFor(PrintFormatPolicy formats, string contentType, PrintOptions? options) =>
        options?.ConverterName ?? formats.RequiredConverterFor(contentType);

    // A named converter says which engine renders the job, so a channel that cannot run it
    // fails the job instead of printing it some other way. PrintOptions.OnUnsupported governs
    // the options a printer cannot apply, and a named engine is not one of those.
    internal static NotSupportedException Unhonoured(string name, string contentType, PrinterId printerId, string reason) =>
        new($"Converter '{name}' cannot render this job, because {reason}. Printer '{printerId}' was sent nothing. " +
            $"To print the '{contentType}' as it is, send the job without PrintOptions.ConverterName, and remove " +
            "the format from PrinterManagerOptions.RequiredConverters if it is required there. " +
            "PrintOptions.OnUnsupported does not apply to a named converter.");

    internal static void ThrowIfNamed(PrintFormatPolicy formats, string contentType, string? name)
    {
        if (name is null)
        {
            return;
        }

        throw new NotSupportedException(
            $"No converter named '{name}' reads '{contentType}'. This process registered {Names(formats, contentType)}.");
    }

    private static string Names(PrintFormatPolicy formats, string contentType)
    {
        var converters = formats.ConvertersFor(contentType);
        return converters.Count == 0
            ? "none for it"
            : String.Join(", ", converters.Select(converter => $"'{converter.Name}'"));
    }
}
