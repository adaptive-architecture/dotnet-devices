namespace AdaptArch.Devices.Printing.Spooler;

// The default queue libcups picks before it asks the server, in the order cupsGetNamedDest
// uses: LPDEST, PRINTER, ~/.cups/lpoptions, then the system lpoptions. The macOS
// "use last printer" preference is not read.
internal static class CupsUserDefault
{
    public static string? Find() =>
        Find(Environment.GetEnvironmentVariable, ReadFile, Environment.IsPrivilegedProcess);

    public static string? Find(Func<string, string?> getEnvironment, Func<string, string?> readFile, bool isRoot)
    {
        var fromEnvironment = getEnvironment("LPDEST");
        if (String.IsNullOrEmpty(fromEnvironment))
        {
            fromEnvironment = getEnvironment("PRINTER");
            if (String.Equals(fromEnvironment, "lp", StringComparison.Ordinal))
            {
                fromEnvironment = null;
            }
        }

        if (!String.IsNullOrEmpty(fromEnvironment))
        {
            return QueueName(fromEnvironment);
        }

        var home = getEnvironment("HOME");
        if (!isRoot && !String.IsNullOrEmpty(home)
            && FromLpoptions(readFile(Path.Combine(home, ".cups", "lpoptions"))) is { } fromUser)
        {
            return fromUser;
        }

        var serverRoot = getEnvironment("CUPS_SERVERROOT");
        return FromLpoptions(readFile(Path.Combine(String.IsNullOrEmpty(serverRoot) ? "/etc/cups" : serverRoot, "lpoptions")));
    }

    private static string? FromLpoptions(string? content)
    {
        if (content is null)
        {
            return null;
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOfAny([' ', '\t']);
            if (separator > 0 && trimmed[..separator].Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return QueueName(trimmed[separator..].TrimStart());
            }
        }

        return null;
    }

    // A destination may carry an instance ("name/instance") and, in lpoptions, options.
    private static string? QueueName(string value)
    {
        var name = value.Split([' ', '\t', '/'], 2)[0];
        return name.Length == 0 ? null : name;
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
