using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

internal sealed class LimitProbe : IDisposable
{
    private const uint CloseMessage = 0x8000;
    private const string ClassName = "WinlandLimitProbe";

    private static readonly ProbeNative.WndProc Proc = WindowProc;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, Track> Limits = [];
    private static readonly Queue<Track> Pending = new();

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private uint _threadId;
    private Exception? _error;

    public LimitProbe()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WinlandLimitProbe",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public IntPtr Anchor { get; private set; }

    public IntPtr Wide { get; private set; }

    public IntPtr Small { get; private set; }

    public void Open()
    {
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The probe windows did not open.");

        if (_error is not null)
            throw new InvalidOperationException("The probe windows did not open.", _error);
    }

    public static void SetMinimum(IntPtr hwnd, int width, int height)
    {
        lock (Gate)
        {
            var track = Limits[hwnd];
            Limits[hwnd] = track with { MinW = width, MinH = height };
        }
    }

    public void Close()
    {
        if (_threadId != 0)
            ProbeNative.PostThreadMessage(_threadId, CloseMessage, 0, 0);

        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(5));

        _ready.Dispose();
    }

    public void Dispose() => Close();

    private void Run()
    {
        try
        {
            _threadId = ProbeNative.GetCurrentThreadId();
            ProbeNative.Register(ClassName, Proc);
            Anchor = Create(new Track(100, 100, 8000, 8000));
            Wide = Create(new Track(1700, 200, 8000, 8000));
            Small = Create(new Track(100, 100, 400, 300));
            _ready.Set();
            Pump();
        }
        catch (Exception exception)
        {
            _error = exception;
            _ready.Set();
        }
    }

    private void Pump()
    {
        while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == CloseMessage)
            {
                ProbeNative.DestroyWindow(Anchor);
                ProbeNative.DestroyWindow(Wide);
                ProbeNative.DestroyWindow(Small);
                ProbeNative.PostQuitMessage(0);
                continue;
            }

            ProbeNative.TranslateMessage(ref message);
            ProbeNative.DispatchMessage(ref message);
        }
    }

    private static IntPtr Create(Track track)
    {
        lock (Gate)
            Pending.Enqueue(track);

        var hwnd = ProbeNative.CreateWindowEx(
            0x00040000,
            ClassName,
            "WinlandLimitProbe",
            0x00CF0000,
            120,
            120,
            320,
            240,
            IntPtr.Zero,
            IntPtr.Zero,
            ProbeNative.Module,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"The probe window was not created ({Marshal.GetLastWin32Error()}).");

        _ = ProbeNative.ShowWindow(hwnd, 5);
        return hwnd;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0024)
        {
            lock (Gate)
            {
                if (!Limits.TryGetValue(hwnd, out var track) && Pending.Count > 0)
                {
                    track = Pending.Dequeue();
                    Limits[hwnd] = track;
                }

                if (Limits.TryGetValue(hwnd, out track))
                    ProbeNative.WriteTrack(lParam, track.MinW, track.MinH, track.MaxW, track.MaxH);
            }

            return IntPtr.Zero;
        }

        return ProbeNative.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private readonly record struct Track(int MinW, int MinH, int MaxW, int MaxH);
}
