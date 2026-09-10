namespace AdaptArch.Devices.Printing.Spooler;

// One shape for the CUPS and Windows spooler drivers. The callers consume this
// interface, not the concrete drivers.
internal interface ISpoolerDriver
{
    Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken);
    Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken);
    Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken);
    Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken);

    // What device the queue prints to. CUPS answers with its device-uri and Windows with
    // the port name; both are the only link between a queue and the device behind it.
    Task<PrinterIdentity?> GetIdentityAsync(string queueName, CancellationToken cancellationToken);
    Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken);
    Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
    Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
}
