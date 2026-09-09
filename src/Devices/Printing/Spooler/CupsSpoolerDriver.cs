using System.Globalization;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Spooler;

// Reaches the CUPS daemon over IPP. CUPS is itself an IPP server listening on
// localhost:631, so this driver needs no native interop: it is the same IPP request
// and response types IppPrinter and IppPrintJobQueue already use, pointed at the
// fixed local daemon path instead of a resolved network endpoint. The daemon path
// never changes, so this driver does not use IppEndpointResolver: there is nothing
// to probe, and probing would double every call's round trips.
internal sealed class CupsSpoolerDriver : ISpoolerDriver
{
    private static readonly Uri DefaultBaseUri = new("ipp://localhost:631/");

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    // Owns the client it builds here, because nothing else reaches the local CUPS
    // daemon and nothing else can share it.
    public CupsSpoolerDriver()
        : this(new HttpClient(), DefaultBaseUri)
    {
    }

    public CupsSpoolerDriver(HttpClient httpClient)
        : this(httpClient, DefaultBaseUri)
    {
    }

    // Lets a test point this driver at a stub server instead of the real daemon.
    internal CupsSpoolerDriver(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

    public async Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        IppOperations operations = new(_httpClient);
        CUPSGetPrintersRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = _baseUri },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetCUPSPrintersAsync(message, token),
            request,
            _baseUri,
            cancellationToken).ConfigureAwait(false);

        var attributes = response.PrintersAttributes ?? [];
        List<DiscoveredPrinter> printers = new(attributes.Length);
        foreach (var printer in attributes)
        {
            // A queue with no name cannot be addressed, so it is not reported.
            if (!String.IsNullOrWhiteSpace(printer.PrinterName))
            {
                printers.Add(MapDiscovered(printer));
            }
        }

        return printers;
    }

    private static DiscoveredPrinter MapDiscovered(PrinterDescriptionAttributes attributes)
    {
        var name = attributes.PrinterName!;
        var id = PrinterId.FromSpooler(name);
        SpoolerPrinterEndpoint endpoint = new(name);
        PrinterInfo info = new(id, attributes.PrinterInfo ?? name)
        {
            Location = attributes.PrinterLocation,
        };
        return new DiscoveredPrinter(id, endpoint, info);
    }

    // The document format follows the payload content type, as IppPrinter does. CUPS
    // accepts application/octet-stream and applies its own filter chain from there.
    public async Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        var uri = QueueUri(queueName);
        using MemoryStream document = new(payload.Data.ToArray());
        PrintJobRequest request = new()
        {
            Document = document,
            OperationAttributes = new()
            {
                PrinterUri = uri,
                DocumentFormat = new DocumentFormat(payload.ContentType, true),
                JobName = options?.JobName,
            },
            JobTemplateAttributes = IppJobTemplateMapper.Map(options),
        };

        IppOperations operations = new(_httpClient);
        var response = await operations.SendAsync(
            static (client, message, token) => client.PrintJobAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var job = response.JobAttributes
            ?? throw new InvalidDataException($"The IPP response from '{uri}' did not include job attributes.");
        return new PrintJobInfo(job.JobId.ToString(CultureInfo.InvariantCulture), PrinterId.FromSpooler(queueName), IppJobStateMapper.Map(job.JobState))
        {
            JobName = options?.JobName,
        };
    }

    public async Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var uri = QueueUri(queueName);
        IppOperations operations = new(_httpClient);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppStatusMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        return IppStatusMapper.Map(PrinterId.FromSpooler(queueName), response.PrinterAttributes, operations.LastRawResponse).Status;
    }

    public async Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var uri = QueueUri(queueName);
        IppOperations operations = new(_httpClient);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppConfigurationMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        return IppConfigurationMapper.Map(PrinterId.FromSpooler(queueName), response.PrinterAttributes);
    }

    public async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var uri = QueueUri(queueName);
        IppOperations operations = new(_httpClient);
        GetJobsRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetJobsAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var printerId = PrinterId.FromSpooler(queueName);
        var attributes = response.JobsAttributes ?? [];
        List<PrintJobInfo> jobs = new(attributes.Length);
        foreach (var job in attributes)
        {
            jobs.Add(IppJobMapper.Map(printerId, job));
        }

        return jobs;
    }

    public async Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (!Int32.TryParse(jobId, out var id))
        {
            // The caller may hold an identifier that came from a different source.
            return null;
        }

        var uri = QueueUri(queueName);
        IppOperations operations = new(_httpClient);
        GetJobAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, JobId = id },
        };

        try
        {
            var response = await operations.SendAsync(
                static (client, message, token) => client.GetJobAttributesAsync(message, token),
                request,
                uri,
                cancellationToken).ConfigureAwait(false);

            return response.JobAttributes is null
                ? null
                : IppJobMapper.Map(PrinterId.FromSpooler(queueName), response.JobAttributes);
        }
        catch (InvalidOperationException) when (IppFailureMapping.StatusCodeOf(operations.LastRawResponse) == IppStatusCode.ClientErrorNotFound)
        {
            // Only client-error-not-found means "unknown job"; see IppPrintJobQueue for
            // the same narrowing and its reasoning.
            return null;
        }
    }

    public async Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (!Int32.TryParse(jobId, out var id))
        {
            return false;
        }

        var uri = QueueUri(queueName);
        IppOperations operations = new(_httpClient);
        CancelJobRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, JobId = id },
        };

        try
        {
            _ = await operations.SendAsync(
                static (client, message, token) => client.CancelJobAsync(message, token),
                request,
                uri,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException) when (IppFailureMapping.StatusCodeOf(operations.LastRawResponse) == IppStatusCode.ClientErrorNotFound)
        {
            return false;
        }
    }

    // "printers/lobby" against "ipp://localhost:631/" gives "ipp://localhost:631/printers/lobby".
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{queueName}");
}
