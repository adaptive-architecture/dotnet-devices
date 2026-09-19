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
    private const string ReadAttributesOperation = "Get-Printer-Attributes";

    public static async Task<PrintJobInfo> SubmitAsync(
        IppContext context,
        Uri uri,
        PrinterId printerId,
        IppSubmission submission,
        CancellationToken cancellationToken)
    {
        var (payload, documentFormat, options, dropped) = submission;
        await using var buffer = MemoryMarshal.TryGetArray(payload.Data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, false)
            : new MemoryStream(payload.Data.ToArray(), false);
        UploadCountingStream document = new(buffer);
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

        IppOperations operations = new(context, uri, "Print-Job", printerId);
        SharpIpp.Models.Responses.PrintJobResponse response;
        IppLog.DocumentSubmitted(context.Logger, documentFormat, uri, buffer.Length);
        try
        {
            response = await operations.SendAsync(
                static (client, message, token) => client.PrintJobAsync(message, token),
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PrinterOperationException exception)
            when (exception.IppStatusCode == IppStatusCodes.ClientErrorDocumentFormatNotSupported)
        {
            // The generic IPP error hides the one cause a caller can act on.
            throw IppFailureMapping.ToUnsupportedDocumentFormat(operations.Call, documentFormat, exception);
        }

        var job = response.JobAttributes
            ?? throw new InvalidDataException($"The IPP response from '{uri}' did not include job attributes.");

        // A printer answers when it accepts the job, which may be before it read the
        // whole document: an early answer aborts the upload without an error. The job
        // exists, so this is reported and not thrown; the watch and the impressions
        // tell whether what arrived prints whole.
        if (document.BytesRead < buffer.Length)
        {
            IppLog.DocumentShortSent(
                context.Logger, uri, job.JobId.ToString(CultureInfo.InvariantCulture), document.BytesRead, buffer.Length, documentFormat);
        }

        // A printer may refuse a job in the answer to the submission itself, so the reasons
        // are read here too and not only on a later read of the queue.
        var reasons = StateReasons.Read(job.JobStateReasons);
        return new PrintJobInfo(job.JobId.ToString(CultureInfo.InvariantCulture), printerId, IppJobStateMapper.Map(job.JobState))
        {
            JobName = options?.JobName,
            DroppedOptions = dropped,
            Detail = StateReasons.Join(reasons),
            StateReasons = reasons,
            StateMessage = IppStatusMapper.Trim(job.JobStateMessage),
            PrinterStateMessage = IppRawAttributes.ReadJobText(operations.LastRawResponse, 0, "job-printer-state-message"),
            RawAttributes = operations.RawAttributes,
        };
    }

    public static async Task<PrinterStatus> GetStatusAsync(IppContext context, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        var details = await GetDetailsAsync(context, uri, printerId, cancellationToken).ConfigureAwait(false);
        return details.Status;
    }

    // The status attribute set has one home: the status client reads the same answer, and a
    // second request here would let the two drift apart.
    public static async Task<IppPrinterDetails> GetDetailsAsync(IppContext context, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, ReadAttributesOperation, printerId);
        var response = await ReadAttributesAsync(operations, uri, IppStatusMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppStatusMapper.Map(
            printerId,
            response.PrinterAttributes,
            operations.LastRawResponse,
            PrinterConnections.From(uri),
            operations.RawAttributes);
    }

    public static async Task<PrinterIdentity> GetIdentityAsync(IppContext context, Uri uri, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, ReadAttributesOperation);
        var response = await ReadAttributesAsync(operations, uri, IppIdentityMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppIdentityMapper.Map(response.PrinterAttributes);
    }

    // A CUPS queue answers with its device-uri, which the typed model does not carry.
    // Asking for no attribute list at all makes CUPS answer with every attribute, which
    // is what carries it.
    public static async Task<PrinterIdentity> GetQueueIdentityAsync(IppContext context, Uri uri, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, ReadAttributesOperation);
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

    public static async Task<PrinterConfiguration> GetConfigurationAsync(IppContext context, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, ReadAttributesOperation, printerId);
        var response = await ReadAttributesAsync(operations, uri, IppConfigurationMapper.RequestedAttributes, cancellationToken).ConfigureAwait(false);
        return IppConfigurationMapper.Map(printerId, response.PrinterAttributes, operations.LastRawResponse);
    }

    public static async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(IppContext context, Uri uri, PrinterId printerId, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, "Get-Jobs", printerId);
        GetJobsRequest request = new()
        {
            // WhichJobs stays unset, so the printer reports its own default set. The
            // attribute list is stated, because a printer left to its own default answers
            // with the job identifier alone and none of the messages that say why it stopped.
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppJobMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetJobsAsync(message, token),
            request,
            cancellationToken).ConfigureAwait(false);

        var attributes = response.JobsAttributes ?? [];
        var raw = operations.LastRawResponse;
        List<PrintJobInfo> jobs = new(attributes.Length);

        // An indexed loop, not a foreach: the raw job groups line up with the typed
        // attributes by position, and a skipped entry would shift every one after it.
        for (var i = 0; i < attributes.Length; i++)
        {
            // RawAttributes stays empty here: one answer must not be copied into every job.
            if (IppJobMapper.Map(printerId, attributes[i], raw, i) is PrintJobInfo mapped)
            {
                IppLog.JobRead(context.Logger, mapped.JobId, uri, mapped.State, mapped.Detail, mapped.PrinterStateMessage ?? mapped.StateMessage);
                jobs.Add(mapped);
            }
        }

        return jobs;
    }

    // Returns null for a non-numeric identifier: the caller may hold a spooler one.
    public static async Task<PrintJobInfo?> GetJobAsync(IppContext context, Uri uri, PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        IppOperations operations = new(context, uri, "Get-Job-Attributes", printerId);
        GetJobAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, JobId = id, RequestedAttributes = IppJobMapper.RequestedAttributes },
        };

        try
        {
            var response = await operations.SendAsync(
                static (client, message, token) => client.GetJobAttributesAsync(message, token),
                request,
                cancellationToken).ConfigureAwait(false);

            if (response.JobAttributes is null)
            {
                return null;
            }

            var job = IppJobMapper.Map(printerId, response.JobAttributes, operations.LastRawResponse, 0, operations.RawAttributes);
            if (job is not null)
            {
                IppLog.JobRead(context.Logger, job.JobId, uri, job.State, job.Detail, job.PrinterStateMessage ?? job.StateMessage);
            }

            return job;
        }
        catch (PrinterOperationException exception) when (exception.IppStatusCode == IppStatusCodes.ClientErrorNotFound)
        {
            // Only not-found means "unknown job". A printer hiccup must not read as one.
            return null;
        }
    }

    public static Task<bool> CancelJobAsync(IppContext context, Uri uri, string jobId, CancellationToken cancellationToken) =>
        CancelJobAsync(context, uri, jobId, null, cancellationToken);

    // The user name is optional because the queue callers never sent one and a printer that
    // never asked for one must keep behaving as it did. The correlation does send one: a
    // server with an owner-based cancel policy refuses to take back an anonymous request's
    // job, which would leave the tracer in the queue.
    public static async Task<bool> CancelJobAsync(IppContext context, Uri uri, string jobId, string? requestingUserName, CancellationToken cancellationToken)
    {
        if (!Int32.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        IppOperations operations = new(context, uri, "Cancel-Job");
        CancelJobRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, JobId = id, RequestingUserName = requestingUserName },
        };

        try
        {
            _ = await operations.SendAsync(
                static (client, message, token) => client.CancelJobAsync(message, token),
                request,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PrinterOperationException exception) when (exception.IppStatusCode == IppStatusCodes.ClientErrorNotFound)
        {
            // Same not-found narrowing as GetJobAsync.
            return false;
        }
    }

    // The jobs that have not finished, as the fingerprint needs them.
    //
    // Unlike GetJobsAsync, this one states which jobs and which attributes it wants. A
    // printer left to its own default answers Get-Jobs with the job identifier alone, and a
    // bare identifier tells one queue from another not at all.
    public static async Task<IReadOnlyList<PrinterQueueFingerprint>> GetQueueFingerprintsAsync(
        IppContext context,
        Uri uri,
        string requestingUserName,
        CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, "Get-Jobs");
        GetJobsRequest request = new()
        {
            OperationAttributes = new()
            {
                PrinterUri = uri,
                WhichJobs = WhichJobs.NotCompleted,

                // Asked for explicitly rather than left unset: the jobs of every user are
                // wanted, and a printer that scopes the answer by default would hand two
                // channels two different views of one queue.
                MyJobs = false,
                RequestingUserName = requestingUserName,
                RequestedAttributes = IppQueueFingerprintMapper.RequestedAttributes,
            },
        };

        var response = await operations.SendAsync(
            static (client, message, token) => client.GetJobsAsync(message, token),
            request,
            cancellationToken).ConfigureAwait(false);

        var attributes = response.JobsAttributes ?? [];
        List<PrinterQueueFingerprint> jobs = new(attributes.Length);
        foreach (var job in attributes)
        {
            if (IppQueueFingerprintMapper.Map(job) is PrinterQueueFingerprint mapped)
            {
                jobs.Add(mapped);
            }
        }

        return jobs;
    }

    // Whether this printer can hold a job that carries no document at all.
    public static async Task<QueueTracerSupport> GetTracerSupportAsync(IppContext context, Uri uri, CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, ReadAttributesOperation);
        var response = await ReadAttributesAsync(operations, uri, TracerSupportAttributes, cancellationToken).ConfigureAwait(false);
        var attributes = response.PrinterAttributes;
        var canCreate = attributes?.OperationsSupported?.Contains(IppOperation.CreateJob) ?? false;
        var canHold = attributes?.JobHoldUntilSupported?.Contains(JobHoldUntil.Indefinite) ?? false;
        return new QueueTracerSupport(canCreate, canHold);
    }

    // Creates a job with no document at all, held indefinitely.
    //
    // Create-Job and no Send-Document is the whole safety of the correlation: a job that
    // holds no document cannot print, whatever a printer does with the hold. The hold is
    // sent as well, so the job does not sit at the head of the queue blocking the next one.
    public static async Task<string?> CreateTracerJobAsync(
        IppContext context,
        Uri uri,
        string jobName,
        string requestingUserName,
        CancellationToken cancellationToken)
    {
        IppOperations operations = new(context, uri, "Create-Job");
        CreateJobRequest request = new()
        {
            OperationAttributes = new()
            {
                PrinterUri = uri,
                JobName = jobName,
                RequestingUserName = requestingUserName,
            },
            JobTemplateAttributes = new() { JobHoldUntil = JobHoldUntil.Indefinite },
        };

        var response = await operations.SendAsync(
            static (client, message, token) => client.CreateJobAsync(message, token),
            request,
            cancellationToken).ConfigureAwait(false);

        return response.JobAttributes?.JobId.ToString(CultureInfo.InvariantCulture);
    }

    private static readonly string[] TracerSupportAttributes =
    [
        "operations-supported",
        "job-hold-until-supported",
    ];

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
            cancellationToken);
    }

    // Counts the octets the HTTP stack pulls for the upload. SharpIppNext hands the
    // document stream over and reads nothing itself, so what is counted here is what
    // left the machine. An early answer from the printer aborts the rest without an
    // error, and only this count can still tell.
    private sealed class UploadCountingStream : Stream
    {
        private readonly Stream _inner;

        public UploadCountingStream(Stream inner) => _inner = inner;

        public long BytesRead { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The upload stream is read-only.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
