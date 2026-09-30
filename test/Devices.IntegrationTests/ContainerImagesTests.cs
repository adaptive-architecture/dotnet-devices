#nullable enable
using System.Diagnostics;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using DotNet.Testcontainers.Builders;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// CI builds the images with a layer cache under the tag the script prints, and the fixtures
/// reuse an image only under the tag they compute. A drift between the two costs no failure,
/// only the cache, so it is caught here instead.
/// </summary>
public class ContainerImagesTests
{
    [Theory]
    [InlineData("cups")]
    [InlineData("ippeve")]
    public async Task TagFor_MatchesThePipelineScript(string directory)
    {
        var root = CommonDirectoryPath.GetGitDirectory().DirectoryPath;
        ProcessStartInfo start = new("sh", [Path.Combine(root, "pipeline", "integration-image-tag.sh"), directory])
        {
            RedirectStandardOutput = true,
        };

        using var script = Process.Start(start)!;
        var printed = await script.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await script.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, script.ExitCode);
        Assert.Equal(printed.Trim(), ContainerImages.TagFor(directory));
    }
}
