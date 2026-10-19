namespace Winland.Windows.Tests;

public class RawInputDevicesTests
{
    // Which devices a machine has is not known here, so this checks only what holds for any of them.
    [Fact]
    public void EachKeyboardAndMouseIsListedOnceWithAName()
    {
        var reader = new RawInputDevices();

        var devices = reader.Read();

        Assert.Equal(devices.Count, devices.Select(device => device.Address).Distinct().Count());
        Assert.All(devices, device => Assert.NotNull(device.Name));
        Assert.Equal(devices, reader.Read());
    }
}
