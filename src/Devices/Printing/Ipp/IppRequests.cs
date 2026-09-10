using System.Globalization;
using System.Runtime.InteropServices;
using AdaptArch.Devices.Printing.Spooler;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// The IPP operations, in one place. A caller differs only in the printer URI and the
// PrinterId it reports, so the network path and the CUPS path cannot drift apart.
internal static class IppRequests
{
    public static async Task<PrintJobInfo> SubmitAsync(
        HttpClient httpClient,
        Uri uri,
        PrinterId printerId,
        PrinterPayload payload,
        string documentFormat,
        PrintOptions? options,
        IReadOnlyList<string> dropped,
        CancellationToken cancellationToken)
    {
        await using var document = MemoryMarshal.TryGetArray(payload.Data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, false)
            : new MemoryStream(payload.Data.ToArray(), false);
        PrintJobRequest request = new()
        {
            Document = document,
            OperationAttributes = new()
            {
                PrinterUri = uri,
                DocumentFormat = new DocumentFormat(documentFormat, true),
                JobName = options?.JobName,
                RequestingUserName = options?.RequestingUserName ?? PrintOptions.DefaultRequestingUserName,
            },
            JobTemplateAttributes = IppJobTemplateMapper.Map(options),
        };

        IppOperations operations = new(httpClient);
        SharpIpp.Models.Responses.PrintJobResponse response;
        try
        {
            response = await operations.SendAsync(
                static (client, message, token) => client.PrintJobAsync(message, token),
                request,
                uri,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
            when (IppFailureMapping.StatusCodeOf(operations.LastRawResponse) == IppStatusCode.ClientErrorDocumentFormatNotSupported)
        {
            // The generic IPP error hides the one cause a caller can act on.
            throw IppFailureMapping.ToUnsupportedDocumentFormat(uri, documentFormat, exception);
        }

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

    public static async Task<PrinterIdentity> GetIdentityAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        var response = await ReadAttributesAsync(operations, uri, IppIdentityMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppIdentityMapper.Map(response.PrinterAttributes);
    }

    // A CUPS queue answers with its device-uri, which the typed model does not carry.
    // Asking for no attribute list at all makes CUPS answer with every attribute, which
    // is what carries it.
    public static async Task<PrinterIdentity> GetQueueIdentityAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        var response = await ReadAttributesAsync(operations, uri, [], cancellationToken).ConfigureAwait(false);
        var identity = IppIdentityMapper.Map(response.PrinterAttributes);
        var deviceUri = IppRawAttributes.ReadText(operations.LastRawResponse, 0, "device-uri");
        return new PrinterIdentity
        {
            // The printer-uuid CUPS reports belongs to the queue, not to the device: it
            // is an MD5 over the server, the port and the queue name, so two queues to
            // one printer report two different values. Only the device URI links them.
            Uuid = null,
            SerialNumber = identity.SerialNumber,
            Manufacturer = identity.Manufacturer,
            Model = identity.Model,
            MakeAndModel = identity.MakeAndModel,
            CommandSets = identity.CommandSets,
            Name = identity.Name,
            Location = identity.Location,
            DeviceUri = deviceUri,
            Aliases = SpoolerAliases.FromDeviceUri(deviceUri),
        };
    }

    public static async Task<PrinterConfiguration> GetConfigurationAsync(HttpClient httpClient, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        var response = await ReadAttributesAsync(operations, uri, IppConfigurationMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppConfigurationMapper.Map(printerId, response.PrinterAttributes, operations.LastRawResponse);
    }

    public static async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(HttpClient httpClient, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(httpClient);
        GetJobsRequest request = new()
        {
            // WhichJobs stays unset, so the printer reports its own default set.
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
            if (IppJobMapper.Map(printerId, job) is PrintJobInfo mapped)
            {
                jobs.Add(mapped);
            }
        }

        return jobs;
    }

    // Returns null for a non-numeric identifier: the caller may hold a spooler one.
    public static async Task<PrintJobInfo?> GetJobAsync(HttpClient httpClient, Uri uri, PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
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
            // Only not-found means "unknown job". A printer hiccup must not read as one.
            return null;
        }
    }

    public static async Task<bool> CancelJobAsync(HttpClient httpClient, Uri uri, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
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
            // Same not-found narrowing as GetJobAsync.
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
