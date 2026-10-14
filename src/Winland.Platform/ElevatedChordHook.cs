using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// Low-level keyboard hook in the elevated helper. While an administrator window is the foreground the desktop hook is not called,
/// so this hook takes the chords the chord file binds. It closes that window itself and sends every other chord to the desktop.
/// Any other window is passed through, and so is a key the desktop hook already published.
/// </summary>
internal static class ElevatedChordHook
{
    private static IElevatedChords? Gate;
    private static readonly ConcurrentQueue<Native.Input[]> Synth = new();
    private static Native.HookProc? _proc;
    private static nint _hook;
    private static nint _foreground;
    private static bool _foregroundElevated;
    private static uint _thread;

    /// <summary>Starts the hook thread. The helper process is already elevated.</summary>
    public static void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-elevated-close",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Run()
    {
        _thread = Native.GetCurrentThreadId();
        var open = ShellBridge.ElevatedChords;
        if (open is null)
        {
            Log.Error("Chord", "elevated chord hook has no chord file");
            return;
        }

        var (gate, watch) = open();
        using (watch)
        {
            Gate = gate;
            try
            {
                ChordKeys.EnsureComplete();
                KeyStroke.EnsureLayout();
                _proc = Hook;
                _hook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _proc, Native.GetModuleHandle(null), 0);
                if (_hook == 0)
                {
                    Log.Error("Chord", "elevated chord hook did not install; Win32 error " + Marshal.GetLastWin32Error());
                    return;
                }

                Log.Information("Chord", "elevated chord hook installed");
                HookStamp.HelperInstalled();
                // A hook call runs inside GetMessage and does not make it return. Enqueue posts a null message,
                // and that is when Flush sends the keys the hook queued.
                while (Native.GetMessage(out var message, 0, 0, 0))
                {
                    if (message.Message != Native.WmNull)
                    {
                        Native.TranslateMessage(ref message);
                        Native.DispatchMessage(ref message);
                    }

                    Flush();
                }

                Native.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
            finally
            {
                Gate = null;
            }
        }
    }

    private static nint Hook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || _hook == 0)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        Native.KbdLlHook info;
        unsafe
        {
            info = *(Native.KbdLlHook*)lParam;
        }

        if ((info.Flags & Native.LlkhfInjected) != 0)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = (int)wParam;
        var down = message is Native.WmKeyDown or Native.WmSysKeyDown;
        var up = message is Native.WmKeyUp or Native.WmSysKeyUp;
        if (!down && !up)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var time = unchecked((int)info.Time);
        var vk = unchecked((int)info.VkCode);
        var saw = HookStamp.DesktopTook(time, vk);
        if (!saw)
        {
            // A key hides the pointer, and the desktop hook did not hear this one.
            HookStamp.HelperHeard(time);
        }

        var elevated = ForegroundElevated();
        var gate = Gate;
        if (gate is null)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var effect = gate.OnKey(ChordKeys.ToPhysical(vk), down, Environment.TickCount64, saw, elevated, HookStamp.DesktopSuperHeld);
        if (gate.Closes(effect))
        {
            CloseForeground();
        }
        else if (gate.Forwards(effect) is QueuedChord chord)
        {
            ElevatedChordPipe.Send(new HookWire.ChordKeyed(chord.Chord, chord.Down));
        }

        if (gate.DesktopSuperWentUp)
        {
            ElevatedChordPipe.Send(new HookWire.Super(Held: false));
        }

        Enqueue(effect);
        if (!effect.Deliver)
        {
            return 1;
        }

        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool ForegroundElevated()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0)
        {
            _foreground = 0;
            _foregroundElevated = false;
            return false;
        }

        if (hwnd != _foreground)
        {
            _foreground = hwnd;
            _foregroundElevated = ShellBridge.Windows.IsElevated(hwnd);
        }

        return _foregroundElevated;
    }

    private static void CloseForeground()
    {
        var hwnd = _foreground;
        if (hwnd == 0)
        {
            return;
        }

        if (Native.PostMessage(hwnd, Native.WmClose, 0, 0))
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                static state => Log.Information("Chord", "close elevated window " + (nint)state!),
                hwnd);
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state => Log.Warning("Chord", "could not close elevated window " + (nint)state!),
            hwnd);
    }

    private static void Enqueue(GateEffect effect)
    {
        if (effect.Inject.Count == 0)
        {
            return;
        }

        var inputs = new Native.Input[effect.Inject.Count];
        for (var i = 0; i < effect.Inject.Count; i++)
        {
            var pulse = effect.Inject[i];
            inputs[i] = pulse.Down ? KeyStroke.Down((ushort)pulse.Id) : KeyStroke.Up((ushort)pulse.Id);
        }

        Synth.Enqueue(inputs);
        var thread = _thread;
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmNull, 0, 0);
        }
    }

    private static void Flush()
    {
        while (Synth.TryDequeue(out var inputs))
        {
            KeyStroke.Send(inputs);
        }
    }
}
