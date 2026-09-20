namespace AdaptArch.Devices.Printing;

// The answer a job gets when it names a converter no registration carries.
//
// Both the IPP path and the Windows spooler path can be asked for one by name, and a caller
// that mistyped it should read the same sentence whichever channel it reached. Naming what is
// registered matters more than usual here: the answer depends on which packages the process
// referenced, which is not something the caller can read off its own code.
internal static class PrintConverters
{
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
