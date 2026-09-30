#nullable enable
using System.Security.Cryptography;
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
        BuildAsync("cups", cancellationToken);

    public static Task<IFutureDockerImage> IppEveAsync(CancellationToken cancellationToken) =>
        BuildAsync("ippeve", cancellationToken);

    /// <summary>
    /// The tag names the content, so an edited Dockerfile gets a new image and an unchanged one
    /// reuses the last. <c>pipeline/integration-image-tag.sh</c> computes the same value for CI,
    /// which builds the images with a layer cache before the tests run: a hash of the files of
    /// the image directory, in ordinal order of their names.
    /// </summary>
    internal static string TagFor(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(DirectoryOf(directory)).Order(StringComparer.Ordinal))
        {
            hash.AppendData(File.ReadAllBytes(file));
        }

        return $"adaptarch-devices-{directory}:{Convert.ToHexStringLower(hash.GetHashAndReset())[..12]}";
    }

    private static string DirectoryOf(string directory) =>
        Path.Combine(CommonDirectoryPath.GetGitDirectory().DirectoryPath, "test", "Devices.IntegrationTests", "docker", directory);

    // Collection fixtures start in parallel, and two builds of one Dockerfile would race for
    // the same tag. The image is kept: a second run reuses it instead of reinstalling the
    // packages.
    private static async Task<IFutureDockerImage> BuildAsync(string directory, CancellationToken cancellationToken)
    {
        var tag = TagFor(directory);
        await BuildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Built.TryGetValue(tag, out var existing))
            {
                return existing;
            }

            var image = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(DirectoryOf(directory))
                .WithDockerfile("Dockerfile")
                .WithName(tag)
                .WithCleanUp(false)
                .WithDeleteIfExists(false)
                // An image that is already here is reused, and an edit changes the tag, so
                // nothing is ever stale. The alternative costs a package install on every
                // run, which is minutes for no answer that changed.
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
