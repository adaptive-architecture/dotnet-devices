using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

/// <summary>
/// Inspects and manages the job queue of a network printer over IPP (Internet Printing Protocol).
/// </summary>
/// <remarks>
/// The endpoint is resolved once and the resolved URI is kept for the life of the instance,
/// because every operation needs the same URI and a printer can take two round trips to probe.
/// A polling job monitor calls this queue repeatedly, so a fresh resolver per call would
/// double every poll's round trips.
/// </remarks>
public sealed class IppPrintJobQueue : IPrintJobQueue
{
    private readonly HttpClient _httpClient;
    private readonly IppEndpointResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrintJobQueue"/> class.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    public IppPrintJobQueue(NetworkPrinterEndpoint endpoint, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _resolver = new IppEndpointResolver(httpClient, endpoint.Host, endpoint.Port, null);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        IppOperations operations = new(_httpClient);
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

    /// <inheritdoc />
    /// <remarks>Returns <c>null</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, out var id))
        {
            // The caller may hold an identifier that came from a spooler rather than IPP.
            return null;
        }

        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    /// <remarks>Returns <c>false</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, out var id))
        {
            return false;
        }

        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
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
            // Same not-found narrowing as GetJobAsync: see the comment there.
            return false;
        }
    }
}
