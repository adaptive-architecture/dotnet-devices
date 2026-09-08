using Xunit;

namespace AdaptArch.Devices.UnitTests;

public class DeviceLibraryTests
{
    [Fact]
    public void DeviceLibrary_ShouldExposeMarkerType()
    {
        Assert.NotNull(typeof(DeviceLibrary));
    }
}
