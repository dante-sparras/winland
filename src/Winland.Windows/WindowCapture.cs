using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Winland.Windows;

/// <summary>
/// Captures one window's picture with Windows.Graphics.Capture, into a texture of the effects' own device. The newest frame is
/// kept, so a window that has not changed still has a picture to draw. The cursor and the capture border are left out.
/// </summary>
internal sealed unsafe class WindowCapture : IDisposable
{
    private static readonly Guid SurfaceAccessId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid InteropId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid ItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    private readonly GpuDevice _gpu;
    private readonly GraphicsCaptureItem _item;
    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private GpuTexture? _picture;
    private bool _disposed;

    private WindowCapture(GpuDevice gpu, GraphicsCaptureItem item, IDirect3DDevice device, Direct3D11CaptureFramePool pool, GraphicsCaptureSession session, SizeInt32 size)
    {
        _gpu = gpu;
        _item = item;
        _device = device;
        _pool = pool;
        _session = session;
        _poolSize = size;
    }

    /// <summary>The newest picture of the window, or null before the first frame has arrived.</summary>
    internal GpuTexture? Picture => _picture;

    /// <summary>How many frames have been copied into <see cref="Picture"/>. It grows when a new frame replaces the picture.</summary>
    internal int Generation { get; private set; }

    /// <summary>The size of that picture in pixels.</summary>
    internal Size Size => _picture is { } picture ? new Size(picture.Width, picture.Height) : default;

    /// <summary>Starts capturing <paramref name="window"/>. Returns null when Windows refuses to capture it.</summary>
    internal static WindowCapture? TryStart(GpuDevice gpu, nint window)
    {
        try
        {
            if (!GraphicsCaptureSession.IsSupported() || gpu.WinRtDevice == 0)
                return null;

            var item = ItemFor(window);
            if (item is null || item.Size.Width <= 0 || item.Size.Height <= 0)
                return null;

            var device = MarshalInterface<IDirect3DDevice>.FromAbi(gpu.WinRtDevice);
            var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                session.IsBorderRequired = false;

            session.StartCapture();
            return new WindowCapture(gpu, item, device, pool, session, item.Size);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or TypeLoadException)
        {
            Console.Error.WriteLine($"A window could not be captured: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Takes the frames that have arrived and keeps the newest one as <see cref="Picture"/>. Returns whether there is a
    /// picture. The caller holds the device's gate.
    /// </summary>
    internal bool Refresh()
    {
        if (_disposed)
            return false;

        try
        {
            Direct3D11CaptureFrame? newest = null;
            while (_pool.TryGetNextFrame() is { } frame)
            {
                newest?.Dispose();
                newest = frame;
            }

            if (newest is null)
                return _picture is not null;

            using (newest)
                return Take(newest);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _session.Dispose();
            _pool.Dispose();
        }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException)
        {
            Console.Error.WriteLine(exception.Message);
        }

        _picture?.Dispose();
        _picture = null;
    }

    private static GraphicsCaptureItem? ItemFor(nint window)
    {
        var activation = ActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem");
        if (activation == 0)
            return null;

        try
        {
            if (GpuCom.QueryInterface(activation, InteropId, out var interop) < 0)
                return null;

            try
            {
                nint abi;
                var itemId = ItemId;
                var created = ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)GpuCom.Table(interop)[3])(interop, window, &itemId, &abi);
                if (created < 0 || abi == 0)
                    return null;

                try
                {
                    return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
                }
                finally
                {
                    GpuCom.Release(ref abi);
                }
            }
            finally
            {
                GpuCom.Release(ref interop);
            }
        }
        finally
        {
            GpuCom.Release(ref activation);
        }
    }

    private bool Take(Direct3D11CaptureFrame frame)
    {
        var size = frame.ContentSize;
        if (size.Width <= 0 || size.Height <= 0)
            return _picture is not null;

        // A window that outgrew the frames needs frames of its new size from now on.
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            _poolSize = size;
        }

        var surface = MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface);
        try
        {
            if (GpuCom.QueryInterface(surface, SurfaceAccessId, out var access) < 0)
                return _picture is not null;

            try
            {
                nint texture;
                var textureId = TextureId;
                var got = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)GpuCom.Table(access)[3])(access, &textureId, &texture);
                if (got < 0 || texture == 0)
                    return _picture is not null;

                try
                {
                    if (_picture is null || _picture.Width != size.Width || _picture.Height != size.Height)
                    {
                        _picture?.Dispose();
                        _picture = _gpu.Create(size.Width, size.Height, GpuUse.Source);
                    }

                    if (_picture is null)
                        return false;

                    _gpu.CopyIn(texture, _picture, size.Width, size.Height);
                    Generation++;
                    return true;
                }
                finally
                {
                    GpuCom.Release(ref texture);
                }
            }
            finally
            {
                GpuCom.Release(ref access);
            }
        }
        finally
        {
            MarshalInterface<IDirect3DSurface>.DisposeAbi(surface);
        }
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(nint className, in Guid iid, out nint factory);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out nint handle);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(nint handle);

    private static nint ActivationFactory(string name)
    {
        if (WindowsCreateString(name, name.Length, out var handle) < 0)
            return 0;

        try
        {
            return RoGetActivationFactory(handle, InspectableId, out var factory) < 0 ? 0 : factory;
        }
        finally
        {
            _ = WindowsDeleteString(handle);
        }
    }

    private static readonly Guid InspectableId = new("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90");
}
