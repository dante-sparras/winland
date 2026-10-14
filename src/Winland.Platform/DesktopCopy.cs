using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// Copies one monitor exactly as the compositor drew it, through DXGI desktop duplication.
/// A GDI copy of the screen misses the system backdrop (Mica and acrylic) under a browser frame or the bar and shows its lighter fallback instead,
/// so a still picture taken that way flashes brighter. The copy is taken in linear scRGB and divided by the monitor's SDR white level,
/// because with HDR on Windows draws the SDR picture at that brightness. The device is made once; the duplication is made for each copy.
/// One thread owns an instance.
/// </summary>
internal sealed unsafe class DesktopCopy : IDisposable
{
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Output1Id = new("00cddea8-939b-4b83-a340-a685226666cc");
    private static readonly Guid Output5Id = new("80a07424-ab52-42eb-833c-0c42fd282d98");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private const int OutputDescSize = 96;
    private const int OutputDescRotation = 84;
    private const int OutputDescMonitor = 88;
    private const int RotationIdentity = 1;
    private const uint BgraSupport = 0x20;
    private const uint SdkVersion = 7;
    private const uint UsageStaging = 3;
    private const uint CpuRead = 0x20000;
    private const uint MapRead = 1;
    private const uint AcquireWaitMs = 60;
    private const int AcquireTries = 3;
    private const string TickClass = "Winland.Freeze";
    private static readonly Native.WndProc TickProc = Native.DefWindowProc;
    private static int _tickClass;
    private const int Popup = unchecked((int)0x80000000);
    private const int Layered = 0x00080000;
    private const int Transparent = 0x00000020;
    private const uint LwaAlpha = 0x2;
    private const uint SwpShowWindow = 0x0040;
    private const uint FormatHalf = 10;
    private const uint FormatBgra = 87;
    private const uint BitmapHeaderSize = 40;

    private nint _device;
    private nint _context;
    private nint _output;
    private nint _monitor;
    private bool _failed;
    private nint _tick;
    private byte _tickAlpha = 1;
    private int _monitorX;
    private int _monitorY;

    // One table per white level for the process. A loupe disposes its copy, and the next pick must not rebuild it.
    private static byte[]? _encode;
    private static double _encodeWhite;

    /// <summary>Copies <paramref name="monitor"/> into a new 32-bit top-down DIB section. Call it on a thread with a message loop.</summary>
    /// <param name="monitor">The monitor handle.</param>
    /// <param name="x">The monitor's left edge in pixels.</param>
    /// <param name="y">The monitor's top edge in pixels.</param>
    /// <param name="width">The monitor's width in pixels.</param>
    /// <param name="height">The monitor's height in pixels.</param>
    /// <returns>The bitmap, which the caller deletes, or zero when duplication is not available.</returns>
    public nint Copy(nint monitor, int x, int y, int width, int height)
    {
        _monitorX = x;
        _monitorY = y;
        if (_failed || monitor == 0 || width <= 0 || height <= 0)
        {
            return 0;
        }

        try
        {
            if (_monitor != monitor && !Open(monitor))
            {
                return 0;
            }

            return Grab(width, height, SdrWhite.For(monitor));
        }
        catch (Exception ex)
        {
            Log.Warning("Tiling", "desktop copy failed", ex);
            Close();
            return 0;
        }
    }

    /// <summary>Gives the device back. The next <see cref="Copy"/> makes one again, which takes some tens of milliseconds.</summary>
    public void Release() => Close();

    public void Dispose()
    {
        Close();
        if (_tick != 0)
        {
            Native.DestroyWindow(_tick);
            _tick = 0;
        }
    }

    /// <summary>Finds the adapter that drives <paramref name="monitor"/> and makes a device on it.</summary>
    private bool Open(nint monitor)
    {
        Close();
        var factoryId = FactoryId;
        if (CreateDXGIFactory1(&factoryId, out var factory) < 0)
        {
            _failed = true;
            return false;
        }

        var desc = stackalloc byte[OutputDescSize];
        try
        {
            for (uint a = 0; ; a++)
            {
                nint adapter;
                if (Call<uint, nint, int>(factory, 7, a, (nint)(&adapter)) < 0)
                {
                    break;
                }

                try
                {
                    for (uint o = 0; ; o++)
                    {
                        nint output;
                        if (Call<uint, nint, int>(adapter, 7, o, (nint)(&output)) < 0)
                        {
                            break;
                        }

                        if (Call<nint, int>(output, 7, (nint)desc) >= 0 && *(nint*)(desc + OutputDescMonitor) == monitor)
                        {
                            // A turned monitor is duplicated the way its panel is scanned, so the picture would lie on its side
                            // or stand on its head. The caller's GDI copy is upright.
                            if (*(int*)(desc + OutputDescRotation) > RotationIdentity)
                            {
                                Release(output);
                                return false;
                            }

                            nint device;
                            nint context;
                            int level;
                            if (D3D11CreateDevice(adapter, 0, 0, BgraSupport, 0, 0, SdkVersion, &device, &level, &context) < 0)
                            {
                                Release(output);
                                return false;
                            }

                            _device = device;
                            _context = context;
                            _output = output;
                            _monitor = monitor;
                            return true;
                        }

                        Release(output);
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return false;
    }

    private nint Grab(int width, int height, double white)
    {
        var duplication = Duplicate();
        if (duplication == 0)
        {
            return 0;
        }

        try
        {
            var info = stackalloc byte[64];
            for (var attempt = 0; attempt < AcquireTries; attempt++)
            {
                nint resource;
                if (Call<uint, nint, nint, int>(duplication, 8, AcquireWaitMs, (nint)info, (nint)(&resource)) < 0)
                {
                    Tick();
                    continue;
                }

                try
                {
                    // A new duplication has no image until something on screen changes. It reports no present time then.
                    if (*(long*)info == 0)
                    {
                        Tick();
                        continue;
                    }

                    var textureId = TextureId;
                    nint texture;
                    if (Call<nint, nint, int>(resource, 0, (nint)(&textureId), (nint)(&texture)) < 0)
                    {
                        return 0;
                    }

                    try
                    {
                        return Read(texture, width, height, white);
                    }
                    finally
                    {
                        Release(texture);
                    }
                }
                finally
                {
                    Release(resource);
                    _ = Call<int>(duplication, 14);
                }
            }

            return 0;
        }
        finally
        {
            Release(duplication);
            if (_tick != 0)
            {
                Native.ShowWindow(_tick, Native.SwHide);
            }
        }
    }

    /// <summary>
    /// Changes one pixel in the monitor's corner by a step of alpha nobody can see, so the compositor draws a frame
    /// and the duplication gets an image of the whole monitor.
    /// </summary>
    private void Tick()
    {
        if (_tick == 0)
        {
            EnsureTickClass();
            var extended = Layered | Transparent | (int)Native.WsExTopmost | Native.WsExToolWindow | Native.WsExNoActivate;
            _tick = Native.CreateWindowEx(extended, TickClass, null, Popup, 0, 0, 1, 1, 0, 0, Native.GetModuleHandle(null), 0);
            if (_tick == 0)
            {
                return;
            }
        }

        _tickAlpha = _tickAlpha == 1 ? (byte)2 : (byte)1;
        _ = SetLayeredWindowAttributes(_tick, 0, _tickAlpha, LwaAlpha);
        Native.SetWindowPos(_tick, Native.HwndTopMost, _monitorX, _monitorY, 1, 1, Native.SwpNoActivate | SwpShowWindow);
    }

    /// <summary>Registers the tick window's class. The fullscreen freeze uses the same class and the same window procedure.</summary>
    private static void EnsureTickClass()
    {
        if (Volatile.Read(ref _tickClass) == 1)
        {
            return;
        }

        var cls = new Native.WndClass
        {
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(TickProc),
            HInstance = Native.GetModuleHandle(null),
            LpszClassName = TickClass,
        };
        if (Native.RegisterClass(ref cls) == 0 && Marshal.GetLastWin32Error() != Native.ErrorClassAlreadyExists)
        {
            Log.Warning("Tiling", "desktop copy did not register its tick window; Win32 error " + Marshal.GetLastWin32Error());
            return;
        }

        Volatile.Write(ref _tickClass, 1);
    }

    /// <summary>
    /// Duplicates the output in linear scRGB, so HDR content and the SDR white level can be handled here.
    /// Windows before IDXGIOutput5 duplicates in 8-bit BGRA.
    /// </summary>
    private nint Duplicate()
    {
        nint duplication;
        var output5Id = Output5Id;
        nint output5;
        if (Call<nint, nint, int>(_output, 0, (nint)(&output5Id), (nint)(&output5)) >= 0)
        {
            var format = FormatHalf;
            var made = Call<nint, uint, uint, nint, nint, int>(output5, 26, _device, 0, 1, (nint)(&format), (nint)(&duplication));
            Release(output5);
            if (made >= 0)
            {
                return duplication;
            }
        }

        var output1Id = Output1Id;
        nint output1;
        if (Call<nint, nint, int>(_output, 0, (nint)(&output1Id), (nint)(&output1)) < 0)
        {
            return 0;
        }

        var legacy = Call<nint, nint, int>(output1, 22, _device, (nint)(&duplication));
        Release(output1);
        return legacy >= 0 ? duplication : 0;
    }

    /// <summary>Copies <paramref name="texture"/> through a staging texture into a DIB section.</summary>
    private nint Read(nint texture, int width, int height, double white)
    {
        var desc = default(TextureDesc);
        Call<nint>(texture, 10, (nint)(&desc));
        if (desc.Format is not (FormatHalf or FormatBgra))
        {
            return 0;
        }

        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.SampleCount = 1;
        desc.SampleQuality = 0;
        desc.Usage = UsageStaging;
        desc.BindFlags = 0;
        desc.CpuAccessFlags = CpuRead;
        desc.MiscFlags = 0;
        nint staging;
        if (Call<nint, nint, nint, int>(_device, 5, (nint)(&desc), 0, (nint)(&staging)) < 0)
        {
            return 0;
        }

        try
        {
            Call<nint, nint>(_context, 47, staging, texture);
            var mapped = default(Mapped);
            if (Call<nint, uint, uint, uint, nint, int>(_context, 14, staging, 0, MapRead, 0, (nint)(&mapped)) < 0)
            {
                return 0;
            }

            try
            {
                var copyWidth = Math.Min(width, (int)desc.Width);
                var copyHeight = Math.Min(height, (int)desc.Height);
                var info = new BitmapInfo
                {
                    Size = BitmapHeaderSize,
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                };
                var bitmap = CreateDIBSection(0, &info, 0, out var bits, 0, 0);
                if (bitmap == 0)
                {
                    return 0;
                }

                var stride = width * 4;
                if (desc.Format == FormatBgra)
                {
                    for (var y = 0; y < copyHeight; y++)
                    {
                        Buffer.MemoryCopy((byte*)mapped.Data + ((long)y * mapped.RowPitch), (byte*)bits + ((long)y * stride), stride, copyWidth * 4);
                    }

                    return bitmap;
                }

                var encode = EncodeTable(white);
                var source = mapped.Data;
                var pitch = mapped.RowPitch;
                var target = bits;
                Parallel.For(0, copyHeight, y =>
                {
                    var from = (ushort*)((byte*)source + ((long)y * pitch));
                    var to = (byte*)target + ((long)y * stride);
                    for (var x = 0; x < copyWidth; x++)
                    {
                        to[(x * 4) + 0] = encode[from[(x * 4) + 2]];
                        to[(x * 4) + 1] = encode[from[(x * 4) + 1]];
                        to[(x * 4) + 2] = encode[from[x * 4]];
                        to[(x * 4) + 3] = 255;
                    }
                });

                return bitmap;
            }
            finally
            {
                Call<nint, uint>(_context, 15, staging, 0);
            }
        }
        finally
        {
            Release(staging);
        }
    }

    /// <summary>A table from each half-float scRGB value to the sRGB byte Windows draws at <paramref name="white"/>.</summary>
    private static byte[] EncodeTable(double white)
    {
        var cached = Volatile.Read(ref _encode);
        if (cached is not null && _encodeWhite == white)
        {
            return cached;
        }

        var table = new byte[65536];
        for (var i = 0; i < table.Length; i++)
        {
            var linear = (double)BitConverter.UInt16BitsToHalf((ushort)i) / white;
            if (double.IsNaN(linear) || linear <= 0)
            {
                continue;
            }

            var encoded = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            table[i] = (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
        }

        _encodeWhite = white;
        Volatile.Write(ref _encode, table);
        return table;
    }

    private void Close()
    {
        Release(_output);
        Release(_context);
        Release(_device);
        _output = 0;
        _context = 0;
        _device = 0;
        _monitor = 0;
    }

    private static void Release(nint unknown)
    {
        if (unknown != 0)
        {
            _ = Call<uint>(unknown, 2);
        }
    }

    private static nint Slot(nint unknown, int index) => (*(nint**)unknown)[index];

    private static TResult Call<TResult>(nint self, int index) =>
        ((delegate* unmanaged[Stdcall]<nint, TResult>)Slot(self, index))(self);

    private static void Call<T1>(nint self, int index, T1 a) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, void>)Slot(self, index))(self, a);

    private static TResult Call<T1, TResult>(nint self, int index, T1 a) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, TResult>)Slot(self, index))(self, a);

    private static void Call<T1, T2>(nint self, int index, T1 a, T2 b) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, T2, void>)Slot(self, index))(self, a, b);

    private static TResult Call<T1, T2, TResult>(nint self, int index, T1 a, T2 b) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, T2, TResult>)Slot(self, index))(self, a, b);

    private static TResult Call<T1, T2, T3, TResult>(nint self, int index, T1 a, T2 b, T3 c) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, T2, T3, TResult>)Slot(self, index))(self, a, b, c);

    private static TResult Call<T1, T2, T3, T4, T5, TResult>(nint self, int index, T1 a, T2 b, T3 c, T4 d, T5 e) =>
        ((delegate* unmanaged[Stdcall]<nint, T1, T2, T3, T4, T5, TResult>)Slot(self, index))(self, a, b, c, d, e);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(Guid* riid, out nint factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, nint* device, int* level, nint* context);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, BitmapInfo* info, uint usage, out nint bits, nint section, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mapped
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Colors;
    }
}
