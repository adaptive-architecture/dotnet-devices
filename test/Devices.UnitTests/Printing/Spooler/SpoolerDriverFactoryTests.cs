using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class SpoolerDriverFactoryTests
{
    [Fact]
    public void Create_SelectsTheDriverForThisOperatingSystem()
    {
        var driver = SpoolerDriverFactory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("WindowsSpoolerDriver", driver.GetType().Name);
        }
        else
        {
            Assert.Equal("CupsSpoolerDriver", driver.GetType().Name);
        }
    }
}
