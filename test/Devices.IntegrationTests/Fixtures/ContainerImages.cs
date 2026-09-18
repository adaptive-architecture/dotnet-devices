#nullable enable
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// Builds the printer images from the Dockerfiles beside this project. They are built and
/// not pulled, so nothing here depends on an image somebody else publishes.
/// </summary>
internal static class ContainerImages
{
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static readonly Dictionary<string, IFutureDockerImage> Built = [];

    public static Task<IFutureDockerImage> CupsAsync(CancellationToken cancellationToken) =>
        BuildAsync("cups", "adaptarch-devices-cups:integration-tests", cancellationToken);

    public static Task<IFutureDockerImage> IppEveAsync(CancellationToken cancellationToken) =>
        BuildAsync("ippeve", "adaptarch-devices-ippeve:integration-tests", cancellationToken);

    // Collection fixtures start in parallel, and two builds of one Dockerfile would race for
    // the same tag. The image is kept: a second run reuses the layer cache instead of
    // reinstalling the packages.
    private static async Task<IFutureDockerImage> BuildAsync(string directory, string tag, CancellationToken cancellationToken)
    {
        await BuildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Built.TryGetValue(tag, out var existing))
            {
                return existing;
            }

            var image = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetGitDirectory(), Path.Combine("test", "Devices.IntegrationTests", "docker", directory))
                .WithDockerfile("Dockerfile")
                .WithName(tag)
                .WithCleanUp(false)
                .WithDeleteIfExists(false)
                // An image that is already here is reused. CI always starts without one, so
                // it always builds; a developer who edits a Dockerfile removes the tag
                // (docker rmi) to get the new one. The alternative costs a package install
                // on every run, which is minutes for no answer that changed.
                .WithImageBuildPolicy(static inspect => inspect is null)
                .Build();

            await image.CreateAsync(cancellationToken).ConfigureAwait(false);
            Built[tag] = image;
            return image;
        }
        finally
        {
            _ = BuildLock.Release();
        }
    }
}
