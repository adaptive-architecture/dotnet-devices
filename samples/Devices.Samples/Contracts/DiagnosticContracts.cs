namespace AdaptArch.Devices.Samples.Contracts;

// Opening a session to every printer, and writing to one, is consent. The request carries
// it, so the server never creates a tracer job the person did not ask for.
internal sealed class CorrelateRequest
{
    public bool Tracer { get; set; }
}

// The Windows spooler checks of docs/windows-manual-tests.md.
internal sealed class WindowsSpoolerRequest
{
    public string Queue { get; set; }

    // Check 3 submits a real job, so it is off until the person asks for it.
    public bool Print { get; set; }
}

// The IPP and the SNMP answer of one host, each as one line or as the reason it failed.
internal sealed record HostDetailsDto(string Host, string Ipp, string Snmp);
