using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class CupsUserDefaultTests
{
    private const string UserFile = "/home/ada/.cups/lpoptions";
    private const string SystemFile = "/etc/cups/lpoptions";

    [Fact]
    public void Find_PrefersLpdestToPrinter() =>
        Assert.Equal("first", Find(new() { ["LPDEST"] = "first", ["PRINTER"] = "second" }));

    [Fact]
    public void Find_IgnoresAPrinterVariableOfLp() =>
        Assert.Null(Find(new() { ["PRINTER"] = "lp" }));

    [Fact]
    public void Find_PrefersTheEnvironmentToTheLpoptionsFiles() =>
        Assert.Equal("env", Find(new() { ["PRINTER"] = "env" }, new() { [UserFile] = "Default user\n" }));

    [Fact]
    public void Find_PrefersTheUserLpoptionsToTheSystemOne() =>
        Assert.Equal("user", Find([], new() { [UserFile] = "Default user\n", [SystemFile] = "Default system\n" }));

    [Fact]
    public void Find_SkipsTheUserLpoptionsForRoot() =>
        Assert.Equal("system", Find([], new() { [UserFile] = "Default user\n", [SystemFile] = "Default system\n" }, isRoot: true));

    [Fact]
    public void Find_ReadsTheSystemLpoptionsFromTheServerRoot() =>
        Assert.Equal("custom", Find(new() { ["CUPS_SERVERROOT"] = "/opt/cups" }, new() { ["/opt/cups/lpoptions"] = "Default custom\n" }));

    [Fact]
    public void Find_TakesTheFirstDefaultLineAndDropsTheInstanceAndOptions() =>
        Assert.Equal("office", Find([], new() { [UserFile] = "Dest lobby sides=one-sided\ndefault office/draft sides=two-sided-long-edge\nDefault lobby\n" }));

    [Fact]
    public void Find_DropsTheInstanceOfAnEnvironmentDestination() =>
        Assert.Equal("office", Find(new() { ["LPDEST"] = "office/draft" }));

    [Fact]
    public void Find_ReturnsNullWhenNothingIsSet() =>
        Assert.Null(Find([]));

    private static string Find(Dictionary<string, string> environment, Dictionary<string, string> files = null, bool isRoot = false)
    {
        environment.TryAdd("HOME", "/home/ada");
        return CupsUserDefault.Find(
            name => environment.GetValueOrDefault(name),
            path => files?.GetValueOrDefault(path),
            isRoot);
    }
}
