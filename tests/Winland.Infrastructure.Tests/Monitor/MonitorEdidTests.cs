
namespace Winland.Infrastructure.Tests;

public class MonitorEdidTests
{
    [Fact]
    public void AnEdidRegistryKeyFollowsTheDisplayConfigTargetPath()
    {
        var key = MonitorEdid.RegistryKey(@"\\?\DISPLAY#DEL40C4#5&2a8c2b4&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

        Assert.Equal(
            @"SYSTEM\CurrentControlSet\Enum\DISPLAY\DEL40C4\5&2a8c2b4&0&UID4352\Device Parameters",
            key);
    }

    [Fact]
    public void ADottedDevicePathNamesTheSameKey()
    {
        var key = MonitorEdid.RegistryKey(@"\\.\DISPLAY#SAM0302#4&1a2b3c4d&0&UID12345#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

        Assert.Equal(
            @"SYSTEM\CurrentControlSet\Enum\DISPLAY\SAM0302\4&1a2b3c4d&0&UID12345\Device Parameters",
            key);
    }

    [Fact]
    public void APathThatIsNotADisplayHasNoEdidKey()
    {
        Assert.Null(MonitorEdid.RegistryKey(@"\\?\USB#VID_1234#5678#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
        Assert.Null(MonitorEdid.RegistryKey(""));
        Assert.Null(MonitorEdid.RegistryKey(null));
        Assert.Null(MonitorEdid.RegistryKey("   "));
    }
}
