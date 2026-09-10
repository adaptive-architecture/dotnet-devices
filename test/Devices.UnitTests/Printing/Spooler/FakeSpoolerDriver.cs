#nullable enable

using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// Records what the printer asked for and returns scripted answers.
internal sealed class FakeSpoolerDriver : ISpoolerDriver
{
    private readonly PrinterConfiguration _configuration;

    public FakeSpoolerDriver(PrinterConfiguration configuration) => _configuration = configuration;

    public List<string> SubmittedQueues { get; } = [];

    public List<PrinterPayload> SubmittedPayloads { get; } = [];

    public int ConfigurationReads { get; private set; }

    // The options the scripted driver reports as not applied.
    public IReadOnlyList<string> DroppedOptions { get; set; } = [];

    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        SubmittedQueues.Add(queueName);
        SubmittedPayloads.Add(payload);
        return Task.FromResult(new PrintJobInfo("11", PrinterId.ForSpooler(queueName), PrintJobState.Queued) { DroppedOptions = DroppedOptions });
    }

    public PrinterIdentity? Identity { get; set; }

    public Task<PrinterIdentity?> GetIdentityAsync(string queueName, CancellationToken cancellationToken) =>
        Task.FromResult(Identity);

    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ConfigurationReads++;
        return Task.FromResult(_configuration);
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterStatus(PrinterId.ForSpooler(queueName), PrinterStatusState.Idle));

    public Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiscoveredPrinter>>([]);

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJobInfo>>([]);

    public Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken) =>
        Task.FromResult<PrintJobInfo?>(null);

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
