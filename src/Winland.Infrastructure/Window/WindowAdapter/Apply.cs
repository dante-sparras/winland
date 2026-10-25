using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Winland.Infrastructure;

/// <summary>Carries out an Outcome: placements, focus, z-order, hiding and showing, closing and killing.</summary>
public sealed partial class WindowAdapter : IDisposable
{
    internal void Apply(Outcome outcome)
    {
        // The step asked for nothing the last outcome did not already do, so no window, border, bar or snapshot is touched.
        if (outcome.IsEmpty)
            return;

        var admitted = _order.Admit(outcome);
        Interlocked.Increment(ref _applying);
        try
        {
            ApplyCore(admitted.Outcome, admitted.Current);
        }
        finally
        {
            Interlocked.Decrement(ref _applying);
        }
    }

    // An outcome that is not current lost its stale parts in Order.Admit. The state every outcome carries
    // whole, the group bars, the input settings and the cursor behavior, is left to the newer outcome that
    // was already applied. Its one-off requests still run.
    private void ApplyCore(Outcome outcome, bool current)
    {
        var windows = new Dictionary<ClientId, WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            windows[client.Id] = client.Window;

        // Once the exit cleanup began, nothing starts a motion that would change alpha or the border again.
        // The frame, effects and alpha refuse changes themselves once they were put back.
        var motions = _releasing ? [] : MotionsOf(outcome);
        foreach (var window in outcome.Hide)
            ConcealOrLeave(window, motions);

        if (current)
            DropClosedFrames(outcome.State.Clients);

        var borderless = new HashSet<WindowId>();
        foreach (var client in outcome.State.Clients)
        {
            if (client.Fullscreen == FullscreenMode.Fullscreen)
                borderless.Add(client.Window);
        }

        foreach (var placement in outcome.Placements)
        {
            if (!windows.TryGetValue(placement.Client, out var window))
                continue;

            var fills = borderless.Contains(window);
            _borderless.Apply(window, fills);
            // A step of a drag moves the dragged window at once, and a window the drag moves out of its way animates.
            if (outcome.Live && !motions.ContainsKey(window))
                PlaceLive(window, placement.Bounds, fills, motions);
            else
                PlaceMoving(window, placement.Bounds, fills, motions);
        }

        foreach (var closing in outcome.Closes)
            CloseOrLeave(closing, motions);

        BeginRest(motions);

        foreach (var window in outcome.Show)
            Reveal(window);

        foreach (var window in outcome.ShowMinimized)
            Reveal(window);

        // The foreground moves first: Windows brings the window it focuses to the top, and the z-order requests then put
        // the floating Clients back above it.
        if (outcome.Focus is WindowId focus)
            _ = _desktop.Foreground(focus);

        Restack(outcome.ZOrder);

        foreach (var launch in outcome.Launches)
        {
            var pid = ProcessLauncher.TryLaunch(launch);
            if (pid is int id)
                _core.NoteLaunched(id, launch);
        }

        var rings = new Dictionary<WindowId, BorderRing>();
        lock (_paintGate)
        {
            if (!_releasing)
            {
                foreach (var window in outcome.ResetDecorations)
                {
                    DropMotion(window);
                    _borderless.Release(window);
                    ResetDecoration(window);
                    _painted.Remove(window);
                }

                foreach (var decoration in outcome.Decorations)
                {
                    if (!windows.TryGetValue(decoration.Client, out var window))
                        continue;

                    Paint(window, decoration);
                    _painted.Add(window);
                    if (decoration.Ring is BorderRing ring)
                        rings[window] = ring;
                }

                // A window that is no longer a Client is painted afresh if it becomes one again.
                if (current)
                    ForgetDwmPainted(windows.Values);
            }
        }

        if (!_releasing && current)
        {
            if (outcome.Decorations.Count > 0 || outcome.ResetDecorations.Count > 0)
                BorderHost.Update(rings);

            GroupBarHost.Update(outcome.GroupBars);
            TrayHost.Update(outcome.Tray, outcome.ConfigNotice);
        }

        if (current)
            SessionEffects.Apply(outcome);
        else
            SessionEffects.ApplyOneOff(outcome);

        if (!_disposed)
            Publish();

        foreach (var kill in outcome.Kills)
            Kill(kill);

        if (outcome.Exit)
            SessionHalt.Request();
    }

    // The owner is read from the window again, because the recorded process can be stale or the frame's host.
    private static void Kill(KillRequest kill)
    {
        long? live = null;
        if (kill.Window is WindowId window)
        {
            var hwnd = new HWND((nint)window.Value);
            if (PInvoke.IsWindow(hwnd))
                live = PackagedIdentity.OwnerProcess(hwnd);
        }

        if (ProcessTerminator.Target(kill.ProcessId, live, pid => pid is > 0 and <= uint.MaxValue ? PackagedIdentity.ExeFile((uint)pid) : "") is long target)
            ProcessTerminator.TryTerminateOnSystem(target);
        else
            Console.Error.WriteLine("The window's process was not ended: it could not be told apart from the frame host.");
    }

    // SW_HIDE removes the window from the switcher: a hidden window is not visible,
    // and the switcher lists visible, uncloaked, ownerless top-level windows.
    // A minimized window stays visible, so it is hidden too. Showing it again uses
    // SW_SHOWMINNOACTIVE while it is iconic, which puts it back without restoring it.
    private void Conceal(WindowId window)
    {
        lock (_showGate)
        {
            if (_releasing)
                return;

            // The hide this causes is reported like an application's own, so it is noted first.
            _concealed[window] = 0;
            ShowOrPost(window, ShowAs.Hide);
        }
    }

    private void Reveal(WindowId window)
    {
        lock (_showGate)
        {
            if (_releasing)
                return;

            ShowConcealed(window);
        }
    }

    private void ShowConcealed(WindowId window)
    {
        _concealed.TryRemove(window, out _);
        ShowOrPost(window, _desktop.Minimized(window) ? ShowAs.ShowMinimized : ShowAs.Show);
    }

    // ShowWindow waits for the window's thread, which a program that has stopped answering does not give. That window is
    // posted the command instead and takes it when the program wakes. A window that answers is waited for, because what
    // follows, such as putting a hidden window back on its Layout rectangle, needs it already hidden.
    private void ShowOrPost(WindowId window, ShowAs show)
    {
        if (_desktop.Hung(window))
            _desktop.ShowAsync(window, show);
        else
            _desktop.Show(window, show);
    }

    internal static string WindowTitle(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        Span<char> buffer = length <= 512 ? stackalloc char[length + 1] : new char[length + 1];
        var written = PInvoke.GetWindowText(hwnd, buffer);
        return written <= 0 ? "" : new string(buffer[..written]);
    }

    private static bool IsShellTaskbar(HWND hwnd)
    {
        var name = ClassName(hwnd);
        return name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    internal static string ClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer);
        return length <= 0 ? "" : new string(buffer[..length]);
    }

    // A layered window input passes through, such as a screen overlay, cannot be clicked or focused, so
    // it is never a Client. Winland draws such surfaces as layers, which it never tiles.
    internal static bool IsOverlay(int extended) =>
        (extended & LayeredStyle) != 0 && (extended & ClickThroughStyle) != 0;

    private static bool IsCloaked(HWND hwnd)
    {
        Span<byte> value = stackalloc byte[sizeof(int)];
        var result = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, value);
        return result.Succeeded && System.Runtime.InteropServices.MemoryMarshal.Read<int>(value) != 0;
    }

    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}
