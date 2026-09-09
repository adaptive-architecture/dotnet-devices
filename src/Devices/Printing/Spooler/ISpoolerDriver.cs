namespace AdaptArch.Devices.Printing.Spooler;

// One shape for the two platform spooler drivers: CupsSpoolerDriver (Linux and macOS, over
// CUPS) and WindowsSpoolerDriver (Windows, over the Win32 print spooler). SpoolerPrinter and
// SpoolerPrintJobQueue consume this interface, not the concrete drivers.
internal interface ISpoolerDriver
{
    Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken);
    Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken);
    Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken);
    Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken);
    Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken);
    Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
    Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken);
}
