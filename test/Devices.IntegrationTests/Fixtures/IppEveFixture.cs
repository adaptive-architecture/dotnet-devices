#nullable enable
using System.Text;
using AdaptArch.Devices.Printing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// An IPP Everywhere printer in a container. The list of formats it advertises is the whole
/// point: the library decides whether to convert a job by reading it, so one printer that
/// names <c>application/pdf</c> and one that does not exercise both branches.
/// </summary>
public abstract class IppEveFixture : IAsyncLifetime
{
    private const ushort ContainerPort = 8631;

    private IContainer? _container;

    protected abstract string Formats { get; }

    protected virtual string PrinterName => "Integration Test Printer";

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(ContainerPort);

    public NetworkPrinterEndpoint Endpoint => NetworkPrinterEndpoint.Ipp(Host, Port);

    public PrinterId Id => PrinterId.ForIpp(Host, Port);

    private IContainer Container => _container ?? throw new InvalidOperationException("The container has not been started.");

    public async ValueTask InitializeAsync()
    {
        var image = await ContainerImages.IppEveAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        _container = new ContainerBuilder(image)
            .WithEnvironment("IPPEVE_FORMATS", Formats)
            .WithEnvironment("IPPEVE_NAME", PrinterName)
            .WithPortBinding(ContainerPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(ContainerPort))
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
    /// Waits for the printer to finish the job it is on. ippeveprinter prints one job at a
    /// time and answers <c>server-error-busy</c> to anything that arrives meanwhile, so a
    /// test that shares the printer waits its turn rather than racing the one before it.
    /// </summary>
    public async Task WaitUntilIdleAsync(CancellationToken cancellationToken)
    {
        using Printing.Ipp.IppPrinter printer = new(Endpoint);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.State == PrinterStatusState.Idle)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads back the documents the printer kept, newest last. <c>ippeveprinter -k</c> keeps
    /// every job it accepted, which is the only honest way to check what was really sent.
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> ReceivedDocumentsAsync(CancellationToken cancellationToken)
    {
        // The .prn files are what the print command wrote, which is nothing. Everything
        // else is a document the printer accepted, kept byte for byte by -k.
        var listing = await Container
            .ExecAsync(["sh", "-c", "ls -1 /spool 2>/dev/null | grep -v '\\.prn$' | sort"], cancellationToken)
            .ConfigureAwait(false);
        var names = listing.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<byte[]> documents = [];
        foreach (var name in names)
        {
            // base64 keeps a raster or a PDF intact on the way out of the container.
            var file = await Container.ExecAsync(["sh", "-c", $"base64 -w0 '/spool/{name}'"], cancellationToken).ConfigureAwait(false);
            documents.Add(Convert.FromBase64String(file.Stdout.Trim()));
        }

        return documents;
    }

    public async Task<string> ReceivedTextAsync(CancellationToken cancellationToken)
    {
        var documents = await ReceivedDocumentsAsync(cancellationToken).ConfigureAwait(false);
        return documents.Count == 0 ? String.Empty : Encoding.UTF8.GetString(documents[^1]);
    }
}

/// <summary>An IPP printer that reads PDF itself, so nothing converts the job.</summary>
public sealed class IppEvePdfFixture : IppEveFixture
{
    public const string CollectionName = "IPP Everywhere, reads PDF";

    protected override string Formats => "application/pdf,image/pwg-raster,image/jpeg,application/octet-stream";
}

[CollectionDefinition(IppEvePdfFixture.CollectionName)]
public sealed class IppEvePdfCollection : ICollectionFixture<IppEvePdfFixture>;

/// <summary>An IPP printer that reads no PDF, which is what makes the library convert one.</summary>
public sealed class IppEveRasterFixture : IppEveFixture
{
    public const string CollectionName = "IPP Everywhere, raster only";

    protected override string Formats => "image/pwg-raster";

    protected override string PrinterName => "Raster Only Printer";
}

[CollectionDefinition(IppEveRasterFixture.CollectionName)]
public sealed class IppEveRasterCollection : ICollectionFixture<IppEveRasterFixture>;
