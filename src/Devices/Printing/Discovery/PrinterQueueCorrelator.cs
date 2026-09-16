using System.Globalization;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

// Proves that two channels reach one print queue, by comparing the jobs each reports.
//
// It is a fifth source of the evidence PrinterDeviceGrouper already merges on, and it obeys
// the same rule: the queue is evidence only when it holds a job distinctive enough that two
// printers could not both be describing it. Everything else is no answer, never a guess.
//
// This type touches no socket. It talks to IQueueEvidenceChannel, so the whole algorithm —
// including the tracer lifetime and the cleanup — is tested without a printer.
internal static class PrinterQueueCorrelator
{
    /// <summary>
    /// A channel to correlate, and the way to talk to it.
    /// </summary>
    internal sealed record Candidate(DiscoveredPrinter Channel, IQueueEvidenceChannel Evidence);

    /// <summary>
    /// Picks the channels worth correlating: one per provisional device, on a transport the
    /// manager may open, and only where no source has already named the device.
    /// </summary>
    /// <remarks>
    /// A channel whose device reported a UUID or a serial number is already grouped, so
    /// reading its queue could only confirm what is known. A second channel of one device
    /// shares its key, so an alias put on the representative reaches it through the union.
    /// </remarks>
    public static IReadOnlyList<DiscoveredPrinter> Choose(
        IReadOnlyList<DiscoveredPrinter> channels,
        IReadOnlyList<PrinterScheme> transports)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(transports);

        var sets = PrinterKeyUnionFind.Seed(channels);
        Dictionary<PrinterDeviceKey, DiscoveredPrinter> chosen = [];
        foreach (var channel in Ordered(channels))
        {
            if (channel.Endpoint.Scheme is not (PrinterScheme.Ipp or PrinterScheme.Ipps)
                || !transports.Contains(channel.Endpoint.Scheme))
            {
                continue;
            }

            var group = sets.Find(channel.Id.DeviceKey);
            if (sets.Best(group).IsDeviceIdentity)
            {
                continue;
            }

            // The first channel of a group wins, and the order is fixed, so the same input
            // always reads the same channels.
            _ = chosen.TryAdd(group, channel);
        }

        List<DiscoveredPrinter> representatives = [.. chosen.Values];
        representatives.Sort(CompareIds);
        return representatives;
    }

    /// <summary>
    /// Correlates the candidates and returns, for each channel that was proved to share a
    /// queue, the keys of the channels it shares it with.
    /// </summary>
    /// <param name="candidates">The channels to correlate, and the evidence seam of each.</param>
    /// <param name="options">The correlation policy.</param>
    /// <param name="allowTracer">Whether this call may write a tracer job. It is <c>false</c> when the manager is only refreshing to resolve an identifier.</param>
    /// <param name="maxConcurrency">How many channels may be read at once.</param>
    /// <param name="logger">The log of the discovery. Use <c>NullLogger.Instance</c> for none.</param>
    /// <param name="cancellationToken">Token to cancel the correlation.</param>
    /// <returns>The proved aliases, by channel identifier. A channel that was proved alone is absent.</returns>
    public static async Task<IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>>> CorrelateAsync(
        IReadOnlyList<Candidate> candidates,
        QueueCorrelationOptions options,
        bool allowTracer,
        int maxConcurrency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        // One channel cannot share a queue with itself, and an arbitrary subset of a large
        // set would depend on which channel sorted first, so both refuse rather than guess.
        if (candidates.Count < 2 || candidates.Count > options.MaxChannels)
        {
            if (candidates.Count > options.MaxChannels)
            {
                // A network with many IPP channels gets no correlation at all, and today
                // nothing says so: the devices simply stay separate.
                DiscoveryLog.CorrelationOverLimit(logger, candidates.Count, options.MaxChannels);
            }
            else
            {
                DiscoveryLog.CorrelationTooFewCandidates(logger, candidates.Count);
            }

            return new Dictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>>();
        }

        var user = options.EffectiveUserName;
        var first = await ReadAllAsync(candidates, user, maxConcurrency, logger, cancellationToken).ConfigureAwait(false);

        PrinterKeyUnionFind proved = new();
        foreach (var candidate in candidates)
        {
            proved.Add(candidate.Channel.Id.DeviceKey);
        }

        JoinBySummary(logger, candidates, first, proved);

        if (allowTracer && options.AllowTracerJob)
        {
            await TraceAsync(candidates, first, proved, options, user, maxConcurrency, logger, cancellationToken).ConfigureAwait(false);
        }

        var groups = Collect(candidates, proved);
        DiscoveryLog.CorrelationCompleted(logger, candidates.Count, groups.Count);
        return groups;
    }

    // Stage A. Two channels whose queues summarise to the same text read one queue.
    private static void JoinBySummary(
        ILogger logger,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?> queues,
        PrinterKeyUnionFind proved)
    {
        Dictionary<string, PrinterDeviceKey> bySummary = [];
        foreach (var id in candidates.Select(static candidate => candidate.Channel.Id))
        {
            if (!queues.TryGetValue(id, out var queue) || queue is null)
            {
                continue;
            }

            // Null means the queue held nothing distinctive. It is no answer, and never a
            // match with another channel that also held nothing.
            if (PrinterQueueFingerprint.Summarize(queue) is not string summary)
            {
                continue;
            }

            var key = id.DeviceKey;
            if (bySummary.TryGetValue(summary, out var seen))
            {
                DiscoveryLog.JoinedByQueue(logger, seen, key);
                proved.Union(seen, key);
            }
            else
            {
                bySummary[summary] = key;
            }
        }
    }

    // Stage B. Put one job that carries no document in every queue that held nothing, then
    // read every queue once more. Every tracer name is unique, so one pass reveals every
    // pairing; submitting to one channel and re-reading the others, a pair at a time, would
    // cost one write per pair for the same answer.
    private static async Task TraceAsync(
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?> first,
        PrinterKeyUnionFind proved,
        QueueCorrelationOptions options,
        string user,
        int maxConcurrency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // A channel whose queue held a distinctive job that nobody else reported is not
        // unproven, it is disproved: its queue is populated and different. Writing to it
        // would cost a job and settle nothing.
        List<Candidate> unproven = [.. candidates.Where(candidate =>
            first.TryGetValue(candidate.Channel.Id, out var queue)
            && queue is not null
            && PrinterQueueFingerprint.Summarize(queue) is null)];

        if (unproven.Count == 0)
        {
            return;
        }

        Dictionary<PrinterId, string> names = [];
        Dictionary<PrinterId, string> created = [];
        Lock guard = new();
        try
        {
            await ForEachAsync(unproven, maxConcurrency, logger, "create-tracer", async (candidate, token) =>
            {
                var support = await candidate.Evidence.ReadTracerSupportAsync(token).ConfigureAwait(false);
                if (!support.IsUsable)
                {
                    // A printer that cannot create a job without a document, or cannot hold
                    // one indefinitely, is never asked to. There is no fallback that sends
                    // a document: that is the one thing this must not do.
                    return;
                }

                var name = QueueCorrelationOptions.TracerJobNamePrefix + Guid.NewGuid().ToString("N");
                lock (guard)
                {
                    names[candidate.Channel.Id] = name;
                }

                // A write to a real printer. It is reported whatever the log level allows
                // below Information, because a customer must be able to see that it happened.
                DiscoveryLog.TracerJobWritten(logger, name, candidate.Channel.Id);
                var jobId = await candidate.Evidence.CreateTracerJobAsync(name, user, token).ConfigureAwait(false);
                if (jobId is not null)
                {
                    lock (guard)
                    {
                        created[candidate.Channel.Id] = jobId;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            if (names.Count == 0)
            {
                return;
            }

            // Every candidate is read again, not only the ones that got a tracer: a channel
            // that showed an empty queue and a channel that showed a populated one can still
            // be one queue seen through two different filters.
            var second = await ReadAllAsync(candidates, user, maxConcurrency, logger, cancellationToken).ConfigureAwait(false);
            JoinByTracer(logger, names, second, proved);
            Sweep(candidates, second, created);
        }
        finally
        {
            await CleanUpAsync(candidates, created, options, user, logger).ConfigureAwait(false);
        }
    }

    // A channel that reports another channel's tracer is that channel's queue.
    private static void JoinByTracer(
        ILogger logger,
        Dictionary<PrinterId, string> names,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?> queues,
        PrinterKeyUnionFind proved)
    {
        Dictionary<string, PrinterId> owners = [];
        foreach ((var id, var name) in names)
        {
            owners[name] = id;
        }

        foreach ((var id, var queue) in queues)
        {
            if (queue is null)
            {
                continue;
            }

            foreach (var job in queue)
            {
                if (job.JobName is string jobName && owners.TryGetValue(jobName, out var owner))
                {
                    DiscoveryLog.JoinedByTracer(logger, owner.DeviceKey, id.DeviceKey, jobName);
                    proved.Union(owner.DeviceKey, id.DeviceKey);
                }
            }
        }
    }

    // Picks up a tracer whose Create-Job answer was lost or carried no identifier: the job
    // is in the queue and names itself, so the re-read finds what the response did not say.
    private static void Sweep(
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?> queues,
        Dictionary<PrinterId, string> created)
    {
        foreach (var id in candidates.Select(static candidate => candidate.Channel.Id))
        {
            if (created.ContainsKey(id)
                || !queues.TryGetValue(id, out var queue)
                || queue is null)
            {
                continue;
            }

            foreach (var job in queue)
            {
                if (job.JobName?.StartsWith(QueueCorrelationOptions.TracerJobNamePrefix, StringComparison.Ordinal) == true)
                {
                    created[id] = job.JobId.ToString(CultureInfo.InvariantCulture);
                    break;
                }
            }
        }
    }

    // The caller's token is deliberately not used. A discovery that was cancelled must still
    // take its job back out of the queue: leaving one behind is worse than one late request.
    private static async Task CleanUpAsync(
        IReadOnlyList<Candidate> candidates,
        Dictionary<PrinterId, string> created,
        QueueCorrelationOptions options,
        string user,
        ILogger logger)
    {
        if (created.Count == 0)
        {
            return;
        }

        using CancellationTokenSource cleanup = new(options.CleanupTimeout);
        foreach (var candidate in candidates)
        {
            if (!created.TryGetValue(candidate.Channel.Id, out var jobId))
            {
                continue;
            }

            // A cancel that fails is survivable by construction: the tracer holds no
            // document, is held indefinitely, and its name says what it is, so a printer
            // that keeps it prints nothing and says who left it.
            var error = await TryAsync(() => candidate.Evidence.CancelTracerJobAsync(jobId, user, cleanup.Token), CancellationToken.None).ConfigureAwait(false);
            if (error is not null)
            {
                // A job left in a customer queue is what they will telephone about.
                DiscoveryLog.TracerJobNotCancelled(logger, jobId, candidate.Channel.Id, error);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?>> ReadAllAsync(
        IReadOnlyList<Candidate> candidates,
        string user,
        int maxConcurrency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Dictionary<PrinterId, IReadOnlyList<PrinterQueueFingerprint>?> queues = [];
        Lock guard = new();
        await ForEachAsync(candidates, maxConcurrency, logger, "read-queue", async (candidate, token) =>
        {
            var queue = await candidate.Evidence.ReadQueueAsync(user, token).ConfigureAwait(false);
            lock (guard)
            {
                queues[candidate.Channel.Id] = queue;
            }
        }, cancellationToken).ConfigureAwait(false);

        return queues;
    }

    // One channel that will not answer costs its own evidence and nothing else.
    private static async Task ForEachAsync(
        IReadOnlyList<Candidate> candidates,
        int maxConcurrency,
        ILogger logger,
        string step,
        Func<Candidate, CancellationToken, Task> body,
        CancellationToken cancellationToken)
    {
        ParallelOptions parallel = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxConcurrency,
        };

        // A channel that did not answer contributes no evidence and stays as the discovery
        // found it. One dead printer must not cost the whole correlation.
        await Parallel.ForEachAsync(
            candidates,
            parallel,
            async (candidate, token) =>
            {
                var error = await TryAsync(() => body(candidate, token), cancellationToken).ConfigureAwait(false);
                if (error is not null)
                {
                    DiscoveryLog.CorrelationStepFailed(logger, step, candidate.Channel.Id, error);
                }
            }).ConfigureAwait(false);
    }

    // Only the caller's own cancellation fails a step. This is the rule PrinterManager
    // already uses for an enrichment read: a timeout also arrives as a cancellation, and
    // must not read as one the caller asked for.
    private static async Task<Exception?> TryAsync(Func<Task> step, CancellationToken cancellationToken)
    {
        try
        {
            await step().ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Given back, not swallowed here: this method knows no printer, and the caller
            // that owns the channel writes the entry.
            return exception;
        }
    }

    private static Dictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>> Collect(
        IReadOnlyList<Candidate> candidates,
        PrinterKeyUnionFind proved)
    {
        Dictionary<PrinterDeviceKey, List<DiscoveredPrinter>> groups = [];
        foreach (var channel in candidates.Select(static candidate => candidate.Channel))
        {
            var group = proved.Find(channel.Id.DeviceKey);
            if (groups.TryGetValue(group, out var members))
            {
                members.Add(channel);
            }
            else
            {
                groups[group] = [channel];
            }
        }

        Dictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>> aliases = [];
        foreach (var members in groups.Values)
        {
            if (members.Count < 2)
            {
                continue;
            }

            // Each side names the other's key, so no new kind of key is invented and
            // PrinterDeviceGrouper merges them exactly as it merges a device URI alias.
            foreach (var id in members.Select(static member => member.Id))
            {
                aliases[id] = [.. members
                    .Where(other => other.Id != id)
                    .Select(static other => other.Id.DeviceKey)
                    .Distinct()];
            }
        }

        return aliases;
    }

    private static List<DiscoveredPrinter> Ordered(IReadOnlyList<DiscoveredPrinter> channels)
    {
        List<DiscoveredPrinter> ordered = [.. channels];
        ordered.Sort(CompareIds);
        return ordered;
    }

    private static int CompareIds(DiscoveredPrinter left, DiscoveredPrinter right) =>
        String.CompareOrdinal(left.Id.ToString(), right.Id.ToString());
}
