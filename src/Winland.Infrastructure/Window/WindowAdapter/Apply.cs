using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Winland.Infrastructure;

/// <summary>Carries out an Outcome: placements, focus, z-order, hiding and showing, closing and killing.</summary>
public sealed partial class WindowAdapter : IDisposable
{
    // Outcomes are decided under the core's lock and applied after it is released, on the loop thread, the pipe threads,
    // the binds worker and the group bar thread. The core queues each one as it decides it, and whichever thread applies
    // next carries out every queued one in that order, so an older outcome never lands after a newer one. The caller's
    // own is among them, so it has been applied when this returns.
    internal void Apply(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // The step asked for nothing the last outcome did not already do, so the core queued nothing. Once the exit
        // cleanup is done, a command or a bind still on its way changes no window.
        if (outcome.IsEmpty || _disposed)
            return;

        // A window event can arrive while this thread waits on a window in the middle of an outcome. The one it decides
        // is queued behind the outcome being applied, and the loop below applies it next.
        if (_applyGate.IsHeldByCurrentThread)
            return;

        lock (_applyGate)
        {
            while (_decided.TryDequeue(out var next))
                ApplyCore(next);
        }
    }

    // The foreground moves first: Windows brings the window it focuses to the top, and the z-order requests then put
    // the floating Clients back above it. Those are held among the topmost windows while it does, so the focused
    // window never shows over them for a frame, and are let go above it.
    private void Front(Outcome outcome)
    {
        List<WindowId> held = [];
        if (outcome.Focus is WindowId focus)
        {
            held = outcome.ZOrder
                .Where(request => request.Top && request.Window != focus && !_desktop.Hung(request.Window))
                .Select(request => request.Window)
                .Where(_desktop.HoldOnTop)
                .ToList();
            _ = _desktop.Foreground(focus);
            foreach (var window in held)
            {
                _desktop.LetGo(window);
                BorderHost.Follow(window);
            }
        }

        // A window let go is above every window that is not topmost already, in the order asked, so it is not raised again.
        Restack(held.Count == 0 ? outcome.ZOrder : [.. outcome.ZOrder.Where(request => !(request.Top && held.Contains(request.Window)))]);
    }

    // Carries out one outcome. Apply calls it for each queued one in turn, and a test calls it with an outcome it built.
    internal void ApplyCore(Outcome outcome)
    {
        var windows = new Dictionary<ClientId, WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            windows[client.Id] = client.Window;

        // Once the exit cleanup began, nothing starts a motion that would change alpha or the border again.
        // The frame, effects and alpha refuse changes themselves once they were put back.
        var motions = _releasing ? [] : MotionsOf(outcome);

        // A window hidden where another is shown, as a Group's member is when the next one takes its tab, is hidden only
        // once the other is shown and has had a frame to paint, so the wallpaper never shows between them. The one shown
        // goes just under it first, so it paints out of sight. One placed anywhere but where a shown window goes is
        // hidden first, so its move is not seen.
        var placed = new Dictionary<WindowId, Rectangle>();
        foreach (var placement in outcome.Placements)
        {
            if (windows.TryGetValue(placement.Client, out var window))
                placed[window] = placement.Bounds;
        }

        var shownAt = new Dictionary<Rectangle, WindowId>();
        foreach (var window in outcome.Show)
        {
            if (placed.TryGetValue(window, out var bounds))
                shownAt[bounds] = window;
        }

        // With nothing focused, hiding the window that has the keyboard would leave Windows to pick a window to
        // activate, which Winland would take for the user's choice. The desktop takes the keyboard first.
        if (outcome.Focus is null && outcome.Hide.Any(_desktop.IsForeground))
            _desktop.ForegroundDesktop();

        var concealLater = new List<WindowId>();
        var under = new List<(WindowId Window, WindowId Above)>();
        foreach (var window in outcome.Hide)
        {
            var at = placed.TryGetValue(window, out var bounds);
            var replacing = at && shownAt.TryGetValue(bounds, out var shown) ? shown : (WindowId?)null;
            if (outcome.Show.Count > 0 && !motions.ContainsKey(window) && (!at || replacing is not null))
            {
                concealLater.Add(window);
                if (replacing is WindowId next && !_desktop.Hung(next) && !_desktop.Hung(window))
                    under.Add((next, window));
            }
            else
            {
                ConcealOrLeave(window, motions);
            }
        }

        foreach (var (window, above) in under)
            _desktop.Under(window, above);

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

            // The window's lock keeps a motion's frame from landing in the middle of the placement.
            lock (_motions.For(window))
            {
                var fills = borderless.Contains(window);
                if (!(fills && _ownFullscreen.ContainsKey(window)))
                    _borderless.Apply(window, fills);
                // A step of a drag moves the dragged window at once, and a window the drag moves out of its way animates.
                if (outcome.Live && !motions.ContainsKey(window))
                    PlaceLive(window, placement.Bounds, fills, motions);
                else
                    PlaceMoving(window, placement.Bounds, fills, motions);
            }
        }

        foreach (var closing in outcome.Closes)
            CloseOrLeave(closing, motions);

        BeginRest(motions);

        foreach (var window in outcome.Show)
            Reveal(window);

        foreach (var window in outcome.ShowMinimized)
            Reveal(window);

        // The window focused here takes the foreground before the windows it replaces are hidden, so Windows never
        // hands the keyboard to a window of its own choosing in between. One that paints under a window it replaces
        // waits until that window is hidden, so it is not raised over it unpainted.
        var focusFirst = outcome.Focus is WindowId early && !under.Exists(pair => pair.Window == early);
        if (focusFirst)
            Front(outcome);

        var rings = new Dictionary<WindowId, BorderRing>();
        foreach (var decoration in outcome.Decorations)
        {
            if (decoration.Ring is BorderRing ring && windows.TryGetValue(decoration.Client, out var window))
                rings[window] = ring;
        }

        if (concealLater.Count > 0)
        {
            // The windows shown get their borders while they paint, and the windows they replace keep theirs until they
            // are hidden.
            if (!_releasing)
                BorderHost.Update(rings, concealLater);

            // A program paints a window shown again when it gets to it. A few frames are waited for at most. The paint
            // is marked done as it begins, so the frames it draws in are waited for too.
            // The windows shown in place of the hidden ones are waited for too, so a Workspace switch does not show a
            // stale or blank frame of a program slow to paint.
            var painting = under.Select(pair => pair.Window)
                .Concat(outcome.Show.Where(window => !_desktop.Hung(window)))
                .Distinct()
                .ToList();
            for (var frame = 0; frame < PaintFrames && !painting.TrueForAll(_desktop.Painted); frame++)
                _desktop.Pace();

            _desktop.Pace();
            if (under.Count > 0)
                _desktop.Pace();
            foreach (var window in concealLater)
                ConcealOrLeave(window, motions);
        }

        if (!focusFirst)
            Front(outcome);

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
                }

                // A window that is no longer a Client is painted afresh if it becomes one again.
                ForgetDwmPainted(windows.Values);
            }
        }

        if (!_releasing)
        {
            if (outcome.Decorations.Count > 0 || outcome.ResetDecorations.Count > 0)
                BorderHost.Update(rings);

            GroupBarHost.Update(outcome.GroupBars);
            TrayHost.Update(outcome.Tray, outcome.ConfigNotice);
        }

        SessionEffects.Apply(outcome);

        if (!_disposed)
            Publish();

        // Starting a program can take a while, so it waits until the borders and bars of this step are drawn.
        foreach (var launch in outcome.Launches)
        {
            var pid = ProcessLauncher.TryLaunch(launch);
            if (pid is int id)
                _core.NoteLaunched(id, launch);
        }

        foreach (var kill in outcome.Kills)
            Kill(kill);

        if (outcome.Exit)
            SessionHalt.Request();
    }

    // The owner is read from the window again, because the recorded process can be stale or the frame's host.
    // Stryker disable once all : reads the real window's process and ends it.
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
        // The show this causes comes back as a window event, which needs nothing read again: Winland knows the window.
        // A window that already showed itself raises no event, so it is not marked, and its next show is read.
        if (_concealed.TryRemove(window, out _) && !_desktop.Shown(window))
            _revealed[window] = 0;
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

    // Stryker disable once all : reads a real window's title.
    internal static string WindowTitle(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        Span<char> buffer = length <= 512 ? stackalloc char[length + 1] : new char[length + 1];
        var written = PInvoke.GetWindowText(hwnd, buffer);
        return written <= 0 ? "" : new string(buffer[..written]);
    }

    // Stryker disable once all : reads a real window's class.
    private static bool IsShellTaskbar(HWND hwnd)
    {
        var name = ClassName(hwnd);
        return name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    // Stryker disable once all : reads a real window's class.
    internal static string ClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer);
        return length <= 0 ? "" : new string(buffer[..length]);
    }

    // A layered window input passes through, such as a screen overlay, cannot be clicked or focused, so
    // it is never a Client. Neither is a window that is never activated, such as a tooltip or a popup a WinUI or
    // Electron app shows over its own window, including one that asks for a taskbar button.
    // Winland draws such surfaces as layers, which it never tiles.
    internal static bool IsOverlay(int extended) =>
        ((extended & LayeredStyle) != 0 && (extended & ClickThroughStyle) != 0)
        || (extended & NoActivateStyle) != 0;

    // Stryker disable once all : asks DWM whether a real window is cloaked.
    internal static bool IsCloaked(HWND hwnd)
    {
        Span<byte> value = stackalloc byte[sizeof(int)];
        var result = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, value);
        return result.Succeeded && System.Runtime.InteropServices.MemoryMarshal.Read<int>(value) != 0;
    }

    // Stryker disable once all : converts the rectangle Windows returns for a real window.
    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}
