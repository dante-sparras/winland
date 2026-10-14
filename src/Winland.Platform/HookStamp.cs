using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace Winland.Platform;

/// <summary>
/// Facts shared by the desktop keyboard hook and the elevated chord hook.
/// The desktop hook publishes the key event it took and the Super it holds. The elevated hook publishes that it is installed,
/// and when it last heard a key.
/// The map, the event, and the byte layout stay inside this module.
/// </summary>
internal static unsafe class HookStamp
{
    private const string MapName = @"Local\Winoma.ChordHook";
    private const string EventName = @"Local\Winoma.ElevatedHook";
    // The desktop hook's last key at 0. The time of the elevated hook's last key at 8. The desktop hook's held Super at 12.
    private const int Capacity = 16;
    private const int HelperKeyOffset = 8;
    private const int SuperOffset = 12;
    private static readonly object Gate = new();
    private static MemoryMappedFile? _map;
    private static MemoryMappedViewAccessor? _view;
    private static byte* _pointer;
    private static EventWaitHandle? _ready;
    // True once the map was opened or could not be. Written last, so a reader that sees it true needs no lock. Every key asks.
    private static volatile bool _prepared;
    private static Exception? _shareFailure;

    /// <summary>A failure to open the shared stamp, if one is waiting. The read clears it so the hook can log it off the key callback.</summary>
    /// <returns>The exception, or null when sharing succeeded.</returns>
    internal static Exception? TakeShareFailure()
    {
        Exception? none = null;
        return Interlocked.Exchange(ref _shareFailure, none);
    }

    /// <summary>The desktop hook took this key event.</summary>
    /// <param name="time">Hook time for the event.</param>
    /// <param name="vk">Virtual key for the event.</param>
    public static void Took(int time, int vk)
    {
        Prepare();
        var pointer = _pointer;
        if (pointer == null)
        {
            return;
        }

        long packed = ((long)(uint)vk << 32) | (uint)time;
        Volatile.Write(ref Unsafe.AsRef<long>(pointer), packed);
    }

    /// <summary>True when the desktop hook already published this same key event.</summary>
    /// <param name="time">Hook time for the event.</param>
    /// <param name="vk">Virtual key for the event.</param>
    /// <returns>True when <see cref="Took"/> last published this event.</returns>
    public static bool DesktopTook(int time, int vk)
    {
        Prepare();
        var pointer = _pointer;
        if (pointer == null)
        {
            return false;
        }

        var packed = Volatile.Read(ref Unsafe.AsRef<long>(pointer));
        return unchecked((int)packed) == time && unchecked((int)(packed >> 32)) == vk;
    }

    /// <summary>The elevated hook heard a physical key. The desktop hook is not called for a key typed into an administrator window.</summary>
    /// <param name="time">Hook time for the event.</param>
    public static void HelperHeard(int time)
    {
        Prepare();
        var pointer = _pointer;
        if (pointer != null)
        {
            Volatile.Write(ref Unsafe.AsRef<int>(pointer + HelperKeyOffset), time);
        }
    }

    /// <summary>Hook time of the last physical key either hook heard, or null when neither has heard one. A time of zero reads as none.</summary>
    public static int? LastKeyTime
    {
        get
        {
            Prepare();
            var pointer = _pointer;
            if (pointer == null)
            {
                return null;
            }

            var desktop = unchecked((int)Volatile.Read(ref Unsafe.AsRef<long>(pointer)));
            var helper = Volatile.Read(ref Unsafe.AsRef<int>(pointer + HelperKeyOffset));
            if (desktop == 0 || helper == 0)
            {
                var only = desktop == 0 ? helper : desktop;
                return only == 0 ? null : only;
            }

            return unchecked(helper - desktop) > 0 ? helper : desktop;
        }
    }

    /// <summary>The desktop hook holds <paramref name="super"/>, or no Super when it is null.</summary>
    /// <param name="super">The held finger.</param>
    public static void DesktopSuper(DesktopSuper? super)
    {
        Prepare();
        var pointer = _pointer;
        if (pointer != null)
        {
            Volatile.Write(ref Unsafe.AsRef<int>(pointer + SuperOffset), Winland.Core.Chords.DesktopSuper.Pack(super));
        }
    }

    /// <summary>The Super the desktop hook holds, or null.</summary>
    public static DesktopSuper? DesktopSuperHeld
    {
        get
        {
            Prepare();
            var pointer = _pointer;
            return pointer == null ? null : Winland.Core.Chords.DesktopSuper.Unpack(Volatile.Read(ref Unsafe.AsRef<int>(pointer + SuperOffset)));
        }
    }

    /// <summary>The elevated chord hook is installed.</summary>
    public static void HelperInstalled()
    {
        Prepare();
        _ready?.Set();
    }

    /// <summary>True once the elevated chord hook has published that it is installed.</summary>
    public static bool HelperIsInstalled
    {
        get
        {
            Prepare();
            var ready = _ready;
            return ready is not null && ready.WaitOne(0);
        }
    }

    private static void Prepare()
    {
        if (_prepared)
        {
            return;
        }

        lock (Gate)
        {
            if (_prepared)
            {
                return;
            }

            try
            {
                _map = MemoryMappedFile.CreateOrOpen(MapName, Capacity);
                _view = _map.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
                _ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName);
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                if (pointer == null)
                {
                    throw new InvalidOperationException("The hook stamp view has no pointer.");
                }

                _pointer = pointer;
                GC.KeepAlive(_map);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _shareFailure, ex);
            }

            _prepared = true;
        }
    }
}
