using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// The keyboards and mice Raw Input lists, each with its handle as its address and its product name as its name: the
/// HID manufacturer and product strings, as Linux names a USB input device, or else the name Device Manager shows,
/// such as <c>HID Keyboard Device</c>. A name is read once for each device path.
/// </summary>
public sealed partial class RawInputDevices : IInputDevices
{
    private const uint MouseType = 0;
    private const uint KeyboardType = 1;
    private const uint DeviceNameCommand = 0x20000007;
    private const uint Failed = uint.MaxValue;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint Success = 0;
    private const uint BufferSmall = 0x1A;

    private static readonly DevicePropertyKey InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    private static readonly DevicePropertyKey DisplayName = new(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<InputDevice> Read()
    {
        var devices = new List<InputDevice>();
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (handle, type) in List())
            {
                if (type is not (MouseType or KeyboardType) || PathOf(handle) is not string path)
                    continue;

                seen.Add(path);
                if (!_names.TryGetValue(path, out var name))
                {
                    name = Describe(path);
                    _names[path] = name;
                }

                devices.Add(new InputDevice(type == MouseType ? InputDeviceKind.Pointer : InputDeviceKind.Keyboard, handle, name));
            }

            foreach (var gone in _names.Keys.Where(path => !seen.Contains(path)).ToList())
                _names.Remove(gone);
        }

        return devices;
    }

    private static unsafe (nint Handle, uint Type)[] List()
    {
        var size = (uint)sizeof(DeviceListEntry);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, size) == Failed || count == 0)
                return [];

            // A device plugged in between the two calls makes the list too small, and the next attempt counts again.
            var entries = new DeviceListEntry[count];
            fixed (DeviceListEntry* first = entries)
            {
                var listed = GetRawInputDeviceList(first, ref count, size);
                if (listed != Failed)
                    return [.. entries.Take((int)listed).Select(entry => (entry.Device, entry.Type))];
            }
        }

        return [];
    }

    private static unsafe string? PathOf(nint device)
    {
        uint length = 0;
        if (GetRawInputDeviceInfo(device, DeviceNameCommand, null, ref length) == Failed || length == 0)
            return null;

        var buffer = new char[length];
        fixed (char* first = buffer)
        {
            var copied = GetRawInputDeviceInfo(device, DeviceNameCommand, first, ref length);
            if (copied == Failed || copied == 0)
                return null;

            var path = new string(first, 0, (int)Math.Min(copied, length)).TrimEnd('\0');

            // Some versions of Windows give the path with \??\ where it opens with \\?\.
            return path.StartsWith(@"\??\", StringComparison.Ordinal) ? @"\\?\" + path[4..] : path;
        }
    }

    private static string Describe(string path)
    {
        var hid = HidName(path);
        return !string.IsNullOrWhiteSpace(hid) ? hid : NodeName(path) ?? "";
    }

    // "manufacturer product", as Linux names a USB HID device, from the strings the device reports.
    private static string? HidName(string path)
    {
        using var file = CreateFile(path, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (file.IsInvalid)
            return null;

        string?[] parts = [HidString(file, Part.Manufacturer), HidString(file, Part.Product)];
        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
    }

    private enum Part
    {
        Manufacturer,
        Product,
    }

    private static unsafe string? HidString(SafeFileHandle file, Part part)
    {
        // A HID string is at most 126 characters and its terminator.
        const int Characters = 127;
        var buffer = stackalloc char[Characters];
        var read = part == Part.Manufacturer
            ? HidD_GetManufacturerString(file, buffer, Characters * sizeof(char))
            : HidD_GetProductString(file, buffer, Characters * sizeof(char));
        if (!read)
            return null;

        var text = new string(buffer, 0, Characters);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    // The name Device Manager shows for the device the interface belongs to.
    private static string? NodeName(string path)
    {
        if (InterfaceString(path, InstanceId) is not string instance || LocateNode(out var node, instance, 0) != Success)
            return null;

        return NodeString(node, DisplayName);
    }

    private static unsafe string? InterfaceString(string path, DevicePropertyKey key)
    {
        uint size = 0;
        if (GetInterfaceProperty(path, key, out _, null, ref size, 0) != BufferSmall || size == 0)
            return null;

        var buffer = new byte[size];
        fixed (byte* first = buffer)
            return GetInterfaceProperty(path, key, out _, first, ref size, 0) == Success ? Text(buffer, size) : null;
    }

    private static unsafe string? NodeString(uint node, DevicePropertyKey key)
    {
        uint size = 0;
        if (GetNodeProperty(node, key, out _, null, ref size, 0) != BufferSmall || size == 0)
            return null;

        var buffer = new byte[size];
        fixed (byte* first = buffer)
            return GetNodeProperty(node, key, out _, first, ref size, 0) == Success ? Text(buffer, size) : null;
    }

    private static string Text(byte[] buffer, uint size) =>
        System.Text.Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0');

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceListEntry
    {
        public nint Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevicePropertyKey(Guid category, uint id)
    {
        private readonly Guid _category = category;
        private readonly uint _id = id;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint GetRawInputDeviceList(DeviceListEntry* list, ref uint count, uint size);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static unsafe partial uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool HidD_GetManufacturerString(SafeFileHandle device, char* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool HidD_GetProductString(SafeFileHandle device, char* buffer, uint length);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_PropertyW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetInterfaceProperty(string path, in DevicePropertyKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint LocateNode(out uint node, string instance, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static unsafe partial uint GetNodeProperty(uint node, in DevicePropertyKey key, out uint type, byte* buffer, ref uint size, uint flags);
}
