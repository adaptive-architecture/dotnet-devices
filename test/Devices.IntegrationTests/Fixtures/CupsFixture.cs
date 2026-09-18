#nullable enable
using AdaptArch.Devices.Printing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// A CUPS daemon with three queues. One daemon serves the whole assembly, so the queues are
/// split by purpose rather than by test: <see cref="RawQueue"/> prints and keeps the bytes,
/// <see cref="HeldQueue"/> never prints so a job stays where a test can look at it, and
/// <see cref="SecondQueue"/> only exists to give the enumeration more than one answer.
/// </summary>
public sealed class CupsFixture : IAsyncLifetime
{
    public const string CollectionName = "CUPS daemon";
    public const string RawQueue = "raw-queue";
    public const string HeldQueue = "held-queue";
    public const string SecondQueue = "second-queue";

    private const ushort ContainerPort = 631;

    private IContainer? _container;

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(ContainerPort);

    /// <summary>The base address the local-spooler driver is pointed at, in place of its fixed <c>ipp://localhost:631/</c>.</summary>
    public Uri BaseUri => new($"ipp://{Host}:{Port}/");

    public CupsPrinterEndpoint EndpointFor(string queue) => new(Host, queue, Port);

    public PrinterId IdFor(string queue) => PrinterId.ForCups(Host, queue, Port);

    private IContainer Container => _container ?? throw new InvalidOperationException("The container has not been started.");

    public async ValueTask InitializeAsync()
    {
        var image = await ContainerImages.CupsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        _container = new ContainerBuilder(image)
            .WithPortBinding(ContainerPort, true)
            // The queues are made by the entrypoint after the daemon answers, so the port
            // being open is not enough: the line it prints once they exist is.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("cups-ready"))
            .Build();

        await _container.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads back the document CUPS spooled for a job, byte for byte as it was submitted.
    /// The daemon keeps it because the image sets <c>PreserveJobFiles Yes</c>, which is a
    /// steadier readback than a backend that writes to a file: it needs no device, and it
    /// works for a queue that is stopped and will never print at all.
    /// </summary>
    public async Task<byte[]> SpooledDocumentAsync(string jobId, CancellationToken cancellationToken)
    {
        var path = $"/var/spool/cups/d{Int32.Parse(jobId, System.Globalization.CultureInfo.InvariantCulture):D5}-001";

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var result = await Container
                .ExecAsync(["sh", "-c", $"[ -s '{path}' ] && base64 -w0 '{path}' || true"], cancellationToken)
                .ConfigureAwait(false);

            var encoded = result.Stdout.Trim();
            if (encoded.Length > 0)
            {
                return Convert.FromBase64String(encoded);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return [];
    }
}

[CollectionDefinition(CupsFixture.CollectionName)]
public sealed class CupsCollection : ICollectionFixture<CupsFixture>;
