namespace AdaptArch.Devices.Printing;

// Reads a state-reason list, and joins it into the one-line Detail the models carried
// before. IPP, SNMP and the operating system spooler all report a set of reasons, so the
// rules live in one place.
internal static class StateReasons
{
    // IPP uses the keyword "none" to say that there is no reason at all (RFC 8011 5.4.12),
    // so it is not a reason. An empty list says the same thing without a magic word.
    public static IReadOnlyList<string> Read<T>(T[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return [];
        }

        List<string> named = [];
        foreach (var value in values)
        {
            var text = value?.ToString();
            if (!String.IsNullOrWhiteSpace(text) && !String.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
            {
                named.Add(text);
            }
        }

        return named;
    }

    // Detail is null when there is nothing to say, never an empty string.
    public static string? Join(IReadOnlyList<string>? reasons) =>
        reasons is null || reasons.Count == 0 ? null : String.Join("; ", reasons);
}
