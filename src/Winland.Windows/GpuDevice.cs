using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Winland.Windows;

/// <summary>Calls on COM objects by their method tables, for the few Direct3D calls the window effects make.</summary>
internal static unsafe class GpuCom
{
    internal static void** Table(nint instance) => *(void***)instance;

    internal static void Release(ref nint instance)
    {
        if (instance == 0)
            return;

        _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Table(instance)[2])(instance);
        instance = 0;
    }

    internal static int QueryInterface(nint instance, in Guid iid, out nint other)
    {
        fixed (Guid* id = &iid)
        fixed (nint* result = &other)
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(instance)[0])(instance, id, result);
    }
}

/// <summary>
/// A Direct3D 11 texture and the views of it a pass needs. The window effects draw into these and read their results back.
/// </summary>
internal sealed class GpuTexture(nint texture, nint shaderView, nint targetView, int width, int height) : IDisposable
{
    private nint _texture = texture;
    private nint _shaderView = shaderView;
    private nint _targetView = targetView;

    internal nint Texture => _texture;

    internal nint ShaderView => _shaderView;

    internal nint TargetView => _targetView;

    internal int Width { get; } = width;

    internal int Height { get; } = height;

    public void Dispose()
    {
        GpuCom.Release(ref _shaderView);
        GpuCom.Release(ref _targetView);
        GpuCom.Release(ref _texture);
    }
}

/// <summary>What a texture is for.</summary>
internal enum GpuUse
{
    /// <summary>A copy of a captured window, read by a pass.</summary>
    Source,

    /// <summary>A texture a pass draws into, which a later pass can read.</summary>
    Target,

    /// <summary>A texture the CPU reads a result from.</summary>
    Readback,
}

/// <summary>
/// The one Direct3D 11 device the window effects draw with, the two shaders they use, and the calls to draw a pass and read it back.
/// All calls go through <see cref="Gate"/>, because a device context is used by one thread at a time.
/// </summary>
internal sealed unsafe class GpuDevice : IDisposable
{
    private const string Shaders = """
        cbuffer C : register(b0) { float4 a; float4 b; float4 c; };
        struct VI { float2 p : POSITION; float2 uv : TEXCOORD0; };
        struct VO { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
        VO vs(VI i)
        {
            VO o;
            o.p = float4(i.p.x / a.x * 2 - 1, 1 - i.p.y / a.y * 2, 0, 1);
            o.uv = i.uv * b.xy + b.zw;
            return o;
        }
        Texture2D tex : register(t0);
        SamplerState lin : register(s0);
        float4 psMesh(VO i) : SV_Target { return tex.SampleLevel(lin, i.uv, 0); }
        float4 psBlur(VO i) : SV_Target
        {
            uint w, h;
            tex.GetDimensions(w, h);
            float2 size = float2(w, h);
            int n = (int)a.z;
            float4 acc = 0;
            [loop] for (int k = 0; k < 64; k++)
            {
                if (k >= n)
                    break;
                float4 box = lerp(b, c, (float)k / n);
                if (box.z <= 0 || box.w <= 0)
                    continue;
                float2 q = b.xy + (i.p.xy - box.xy) / box.zw * b.zw;
                if (q.x < 0 || q.y < 0 || q.x >= a.x || q.y >= a.y)
                    continue;
                acc += tex.SampleLevel(lin, q / size, 0);
            }
            if (acc.a == 0)
                return 0;
            return acc / n;
        }
        """;

    private const int BgraFormat = 87;
    private const int Vertices = 8192;
    private const int ConstantBytes = 48;

    private nint _device;
    private nint _context;
    private nint _vertexShader;
    private nint _meshShader;
    private nint _blurShader;
    private nint _layout;
    private nint _sampler;
    private nint _rasterizer;
    private nint _constants;
    private nint _vertices;
    private bool _disposed;

    private GpuDevice()
    {
    }

    /// <summary>Held by whoever draws, reads back or disposes.</summary>
    internal Lock Gate { get; } = new();

    /// <summary>The device wrapped for Windows.Graphics.Capture, or null when it could not be made.</summary>
    internal nint Dxgi { get; private set; }

    internal nint Raw => _device;

    /// <summary>Makes the device and its shaders. Returns null when the machine cannot draw with them.</summary>
    internal static GpuDevice? TryCreate()
    {
        var gpu = new GpuDevice();
        try
        {
            if (gpu.Open())
                return gpu;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or COMException)
        {
            Console.Error.WriteLine($"Window effects are unavailable: {exception.Message}");
        }

        gpu.Dispose();
        return null;
    }

    /// <summary>Whether the device was removed or reset, so it can draw no more.</summary>
    internal bool Lost()
    {
        if (_device == 0)
            return true;

        return ((delegate* unmanaged[Stdcall]<nint, int>)GpuCom.Table(_device)[39])(_device) < 0;
    }

    internal GpuTexture? Create(int width, int height, GpuUse use)
    {
        if (width <= 0 || height <= 0 || _device == 0)
            return null;

        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = (DXGI_FORMAT)BgraFormat,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = use == GpuUse.Readback ? D3D11_USAGE.D3D11_USAGE_STAGING : D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = use switch
            {
                GpuUse.Source => D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                GpuUse.Target => D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                _ => 0,
            },
            CPUAccessFlags = use == GpuUse.Readback ? D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ : 0,
        };

        nint texture;
        if (((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void*, nint*, int>)GpuCom.Table(_device)[5])(_device, &description, null, &texture) < 0)
            return null;

        nint shaderView = 0;
        nint targetView = 0;
        if (use != GpuUse.Readback
            && ((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)GpuCom.Table(_device)[7])(_device, texture, null, &shaderView) < 0)
        {
            GpuCom.Release(ref texture);
            return null;
        }

        if (use == GpuUse.Target
            && ((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)GpuCom.Table(_device)[9])(_device, texture, null, &targetView) < 0)
        {
            GpuCom.Release(ref shaderView);
            GpuCom.Release(ref texture);
            return null;
        }

        return new GpuTexture(texture, shaderView, targetView, width, height);
    }

    /// <summary>Copies the top left of a texture another object made on this device, such as a captured frame.</summary>
    internal void CopyIn(nint source, GpuTexture destination, int width, int height)
    {
        if (_disposed || _context == 0)
            return;

        var box = new D3D11_BOX { left = 0, top = 0, front = 0, right = (uint)width, bottom = (uint)height, back = 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)GpuCom.Table(_context)[46])(
            _context, destination.Texture, 0, 0, 0, 0, source, 0, &box);
    }

    /// <summary>
    /// Clears <paramref name="target"/> and draws triangles into the top left of it, <paramref name="width"/> by
    /// <paramref name="height"/> pixels. The vertices are positions in those pixels and places in the picture from 0 to 1.
    /// <paramref name="blur"/> chooses the pass that averages the boxes the constants describe.
    /// </summary>
    internal bool Draw(GpuTexture target, int width, int height, GpuTexture source, ReadOnlySpan<float> constants, ReadOnlySpan<MeshVertex> vertices, bool blur)
    {
        if (_disposed || vertices.Length == 0 || vertices.Length > Vertices || constants.Length * sizeof(float) != ConstantBytes)
            return false;

        var table = GpuCom.Table(_context);
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)table[14])(_context, _vertices, 0, 4, 0, &mapped) < 0)
            return false;

        var floats = (float*)mapped.pData;
        for (var i = 0; i < vertices.Length; i++)
        {
            floats[i * 4] = vertices[i].X;
            floats[i * 4 + 1] = vertices[i].Y;
            floats[i * 4 + 2] = vertices[i].U;
            floats[i * 4 + 3] = vertices[i].V;
        }

        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)table[15])(_context, _vertices, 0);

        fixed (float* values = constants)
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, void*, void*, uint, uint, void>)table[48])(_context, _constants, 0, null, values, 0, 0);

        var clear = stackalloc float[4] { 0, 0, 0, 0 };
        var targetView = target.TargetView;
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)table[50])(_context, targetView, clear);
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)table[33])(_context, 1, &targetView, 0);
        var viewport = new D3D11_VIEWPORT { TopLeftX = 0, TopLeftY = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
        ((delegate* unmanaged[Stdcall]<nint, uint, D3D11_VIEWPORT*, void>)table[44])(_context, 1, &viewport);
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)table[43])(_context, _rasterizer);
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)table[17])(_context, _layout);
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)table[24])(_context, 4);
        var buffer = _vertices;
        var stride = 16u;
        var offset = 0u;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, uint*, uint*, void>)table[18])(_context, 0, 1, &buffer, &stride, &offset);
        var constantBuffer = _constants;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)table[7])(_context, 0, 1, &constantBuffer);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)table[16])(_context, 0, 1, &constantBuffer);
        ((delegate* unmanaged[Stdcall]<nint, nint, void*, uint, void>)table[11])(_context, _vertexShader, null, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, void*, uint, void>)table[9])(_context, blur ? _blurShader : _meshShader, null, 0);
        var sampler = _sampler;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)table[10])(_context, 0, 1, &sampler);
        var view = source.ShaderView;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)table[8])(_context, 0, 1, &view);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, void>)table[13])(_context, (uint)vertices.Length, 0);

        // The target is read by the next pass, so it is taken off the pipeline.
        nint none = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)table[33])(_context, 1, &none, 0);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)table[8])(_context, 0, 1, &none);
        return true;
    }

    /// <summary>Copies the top left of <paramref name="source"/> to <paramref name="staging"/> and gives its rows to <paramref name="read"/>.</summary>
    internal bool Read(GpuTexture source, GpuTexture staging, int width, int height, ReadRows read)
    {
        if (_disposed)
            return false;

        var table = GpuCom.Table(_context);
        var box = new D3D11_BOX { left = 0, top = 0, front = 0, right = (uint)width, bottom = (uint)height, back = 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)table[46])(
            _context, staging.Texture, 0, 0, 0, 0, source.Texture, 0, &box);
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)table[14])(_context, staging.Texture, 0, 1, 0, &mapped) < 0)
            return false;

        try
        {
            read((byte*)mapped.pData, (int)mapped.RowPitch);
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)table[15])(_context, staging.Texture, 0);
        }

        return true;
    }

    /// <summary>Hands the rows of a mapped texture over, as a pointer to the first and the bytes from one row to the next.</summary>
    internal delegate void ReadRows(byte* rows, int pitch);

    public void Dispose()
    {
        lock (Gate)
        {
            _disposed = true;
            if (_context != 0)
            {
                ((delegate* unmanaged[Stdcall]<nint, void>)GpuCom.Table(_context)[109])(_context);
                ((delegate* unmanaged[Stdcall]<nint, void>)GpuCom.Table(_context)[110])(_context);
            }

            GpuCom.Release(ref _vertices);
            GpuCom.Release(ref _constants);
            GpuCom.Release(ref _rasterizer);
            GpuCom.Release(ref _sampler);
            GpuCom.Release(ref _layout);
            GpuCom.Release(ref _blurShader);
            GpuCom.Release(ref _meshShader);
            GpuCom.Release(ref _vertexShader);
            var dxgi = Dxgi;
            Dxgi = 0;
            GpuCom.Release(ref dxgi);
            var winrt = WinRtDevice;
            WinRtDevice = 0;
            GpuCom.Release(ref winrt);
            GpuCom.Release(ref _context);
            GpuCom.Release(ref _device);
        }
    }

    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint count, uint sdk, out nint device, out int level, out nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("d3dcompiler_47.dll")]
    private static extern int D3DCompile(byte* source, nuint size, byte* name, void* defines, void* include, byte* entry, byte* target, uint flags1, uint flags2, out nint code, out nint errors);

    /// <summary>The Direct3D device Windows.Graphics.Capture draws frames with, as the ABI pointer of an IDirect3DDevice.</summary>
    internal nint WinRtDevice { get; private set; }

    private bool Open()
    {
        // D3D_DRIVER_TYPE_HARDWARE and D3D11_CREATE_DEVICE_BGRA_SUPPORT. Only a real adapter draws fast enough for a frame.
        if (D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out _device, out _, out _context) < 0 || _device == 0)
            return false;

        if (GpuCom.QueryInterface(_device, DxgiDeviceId, out var dxgi) < 0)
            return false;

        Dxgi = dxgi;
        if (CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var winrt) < 0)
            return false;

        WinRtDevice = winrt;
        var vertexCode = Compile("vs", "vs_4_0");
        var meshCode = Compile("psMesh", "ps_4_0");
        var blurCode = Compile("psBlur", "ps_4_0");
        if (vertexCode.Length == 0 || meshCode.Length == 0 || blurCode.Length == 0)
            return false;

        if (!MakeShader(12, vertexCode, out _vertexShader) || !MakeShader(15, meshCode, out _meshShader) || !MakeShader(15, blurCode, out _blurShader))
            return false;

        return MakeLayout(vertexCode) && MakeStates() && MakeBuffers();
    }

    private static byte[] Compile(string entry, string target)
    {
        var source = Encoding.ASCII.GetBytes(Shaders);
        var entryName = Encoding.ASCII.GetBytes(entry + "\0");
        var targetName = Encoding.ASCII.GetBytes(target + "\0");
        nint code;
        nint errors;
        int result;
        fixed (byte* text = source)
        fixed (byte* entryPointer = entryName)
        fixed (byte* targetPointer = targetName)
            result = D3DCompile(text, (nuint)source.Length, null, null, null, entryPointer, targetPointer, 1u << 15, 0, out code, out errors);

        try
        {
            if (result < 0 || code == 0)
            {
                if (errors != 0)
                    Console.Error.WriteLine(Marshal.PtrToStringAnsi(BlobPointer(errors), BlobSize(errors)));
                return [];
            }

            var bytes = new byte[BlobSize(code)];
            Marshal.Copy(BlobPointer(code), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GpuCom.Release(ref code);
            GpuCom.Release(ref errors);
        }
    }

    private static nint BlobPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nint>)GpuCom.Table(blob)[3])(blob);

    private static int BlobSize(nint blob) => (int)((delegate* unmanaged[Stdcall]<nint, nuint>)GpuCom.Table(blob)[4])(blob);

    private bool MakeShader(int slot, byte[] code, out nint shader)
    {
        nint made;
        fixed (byte* bytes = code)
        {
            var result = ((delegate* unmanaged[Stdcall]<nint, void*, nuint, void*, nint*, int>)GpuCom.Table(_device)[slot])(_device, bytes, (nuint)code.Length, null, &made);
            shader = result < 0 ? 0 : made;
            return result >= 0;
        }
    }

    private bool MakeLayout(byte[] vertexCode)
    {
        var position = Encoding.ASCII.GetBytes("POSITION\0");
        var texture = Encoding.ASCII.GetBytes("TEXCOORD\0");
        fixed (byte* positionName = position)
        fixed (byte* textureName = texture)
        fixed (byte* code = vertexCode)
        {
            var elements = stackalloc D3D11_INPUT_ELEMENT_DESC[2];
            elements[0] = new D3D11_INPUT_ELEMENT_DESC
            {
                SemanticName = new global::Windows.Win32.Foundation.PCSTR(positionName),
                SemanticIndex = 0,
                Format = (DXGI_FORMAT)16,
                InputSlot = 0,
                AlignedByteOffset = 0,
                InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                InstanceDataStepRate = 0,
            };
            elements[1] = new D3D11_INPUT_ELEMENT_DESC
            {
                SemanticName = new global::Windows.Win32.Foundation.PCSTR(textureName),
                SemanticIndex = 0,
                Format = (DXGI_FORMAT)16,
                InputSlot = 0,
                AlignedByteOffset = 8,
                InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                InstanceDataStepRate = 0,
            };
            nint layout;
            var result = ((delegate* unmanaged[Stdcall]<nint, D3D11_INPUT_ELEMENT_DESC*, uint, void*, nuint, nint*, int>)GpuCom.Table(_device)[11])(
                _device, elements, 2, code, (nuint)vertexCode.Length, &layout);
            _layout = result < 0 ? 0 : layout;
            return result >= 0;
        }
    }

    private bool MakeStates()
    {
        // Linear filtering, clamped at the edges.
        var sampler = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            MaxAnisotropy = 1,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
        };
        nint samplerState;
        if (((delegate* unmanaged[Stdcall]<nint, D3D11_SAMPLER_DESC*, nint*, int>)GpuCom.Table(_device)[23])(_device, &sampler, &samplerState) < 0)
            return false;

        _sampler = samplerState;

        // No culling, so a mesh drawn mirrored by a wobble is still seen.
        var rasterizer = new D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID,
            CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE,
            DepthClipEnable = true,
        };
        nint rasterizerState;
        if (((delegate* unmanaged[Stdcall]<nint, D3D11_RASTERIZER_DESC*, nint*, int>)GpuCom.Table(_device)[22])(_device, &rasterizer, &rasterizerState) < 0)
            return false;

        _rasterizer = rasterizerState;
        return true;
    }

    private bool MakeBuffers()
    {
        var constants = new D3D11_BUFFER_DESC
        {
            ByteWidth = ConstantBytes,
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
        };
        nint constantBuffer;
        if (((delegate* unmanaged[Stdcall]<nint, D3D11_BUFFER_DESC*, void*, nint*, int>)GpuCom.Table(_device)[3])(_device, &constants, null, &constantBuffer) < 0)
            return false;

        _constants = constantBuffer;
        var vertices = new D3D11_BUFFER_DESC
        {
            ByteWidth = Vertices * 16,
            Usage = D3D11_USAGE.D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER,
            CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE,
        };
        nint vertexBuffer;
        if (((delegate* unmanaged[Stdcall]<nint, D3D11_BUFFER_DESC*, void*, nint*, int>)GpuCom.Table(_device)[3])(_device, &vertices, null, &vertexBuffer) < 0)
            return false;

        _vertices = vertexBuffer;
        return true;
    }
}
