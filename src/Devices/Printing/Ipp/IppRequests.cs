using System.Globalization;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// The IPP operations, in one place. Every caller differs in only two ways: how it gets
// the printer URI, and which PrinterId it reports. Both arrive here as parameters, so a
// network printer that resolves its endpoint and a CUPS queue at a fixed daemon path run
// the same code. Keeping one copy is what stops the two paths drifting apart.
internal static class IppRequests
{
    // Sends a document and reports the job the printer created. The caller supplies the
    // options it already validated, and the list of options it dropped, so one submit
    // path fills DroppedOptions for every caller.
    public static async Task<PrintJobInfo> SubmitAsync(
        HttpClient httpClient,
        Uri uri,
        PrinterId printerId,
        PrinterPayload payload,
        PrintOptions? options,
        IReadOnlyList<string> dropped,
        CancellationToken cancellationToken)
    {
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

        IppOperations operations = new(httpClient);
        var response = await operations.SendAsync(
            static (client, message, token) => client.PrintJobAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var job = response.JobAttributes
            ?? throw new InvalidDataException($"The IPP response from '{uri}' did not include job attributes.");
        return new PrintJobInfo(job.JobId.ToString(CultureInfo.InvariantCulture), printerId, IppJobStateMapper.Map(job.JobState))
        {
            JobName = options?.JobName,
            DroppedOptions = dropped,
        };
    }

    public static async Task<PrinterStatus> GetStatusAsync(HttpClient httpClient, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        var response = await ReadAttributesAsync(operations, uri, IppStatusMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppStatusMapper.Map(printerId, response.PrinterAttributes, operations.LastRawResponse).Status;
    }

    public static async Task<PrinterConfiguration> GetConfigurationAsync(HttpClient httpClient, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        var response = await ReadAttributesAsync(operations, uri, IppConfigurationMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppConfigurationMapper.Map(printerId, response.PrinterAttributes);
    }

    public static async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(HttpClient httpClient, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        GetJobsRequest request = new()
        {
            // OperationAttributes.WhichJobs is left unset, so the printer reports its own
            // default set instead of a caller-chosen subset.
            OperationAttributes = new() { PrinterUri = uri },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetJobsAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var attributes = response.JobsAttributes ?? [];
        List<PrintJobInfo> jobs = new(attributes.Length);
        foreach (var job in attributes)
        {
            jobs.Add(IppJobMapper.Map(printerId, job));
        }

        return jobs;
    }

    // Returns null when the identifier is not a number, because the caller may hold an
    // identifier that came from a spooler rather than from IPP.
    public static async Task<PrintJobInfo?> GetJobAsync(HttpClient httpClient, Uri uri, PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, out var id))
        {
            return null;
        }

        IppOperations operations = new(httpClient);
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

            return response.JobAttributes is null ? null : IppJobMapper.Map(printerId, response.JobAttributes);
        }
        catch (InvalidOperationException) when (IppFailureMapping.StatusCodeOf(operations.LastRawResponse) == IppStatusCode.ClientErrorNotFound)
        {
            // Only client-error-not-found means "unknown job". Every other IPP error (a
            // transient server error, a rejected request, and so on) propagates instead of
            // reading as "unknown job" too: a caller such as the polling job monitor
            // must not mistake a printer hiccup for the job having left the queue. The
            // filter not matching lets the exception continue on its own, unchanged.
            return null;
        }
    }

    public static async Task<bool> CancelJobAsync(HttpClient httpClient, Uri uri, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, out var id))
        {
            return false;
        }

        IppOperations operations = new(httpClient);
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
            // Same not-found narrowing as GetJobAsync: see the comment there.
            return false;
        }
    }

    private static Task<SharpIpp.Models.Responses.GetPrinterAttributesResponse> ReadAttributesAsync(
        IppOperations operations,
        Uri uri,
        string[] requestedAttributes,
        CancellationToken cancellationToken)
    {
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = requestedAttributes },
        };
        return operations.SendAsync(
            static (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken);
    }
}
