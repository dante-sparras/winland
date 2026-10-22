namespace Winland.Infrastructure;

/// <summary>
/// The name each device handle has in the <c>devices</c> reply. The reply and this cache both run
/// <see cref="InputDeviceNames.Assign"/> on one read of the same device list, so a duplicated name gets the same
/// suffix in both. The map keeps one kind, keyboards unless a caller asks for pointers, and the hook reads it
/// without waiting on the refresh.
/// </summary>
internal sealed class DeviceNameCache
{
    private volatile Dictionary<long, string> _names = new();

    internal void Replace(IReadOnlyList<InputDevice> devices, InputDeviceKind kind = InputDeviceKind.Keyboard)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var assigned = InputDeviceNames.Assign(devices);
        var map = new Dictionary<long, string>(devices.Count);
        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].Kind == kind)
                map[devices[i].Address] = assigned[i];
        }

        _names = map;
    }

    internal bool TryName(long address, out string name) => _names.TryGetValue(address, out name!);
}
