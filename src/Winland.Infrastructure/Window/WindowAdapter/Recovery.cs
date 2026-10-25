using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Infrastructure;

/// <summary>Putting windows back after a crash, and saving what this run changed on each window so the next run can.</summary>
public sealed partial class WindowAdapter : IDisposable
{
    public void Recover(RecoverySnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (snapshot is null || snapshot.Clients.Count == 0)
            return;

        var kept = new List<RecoveredClient>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
        {
            var recognized = WindowIdentity.Recognize(client);
            if (recognized == WindowIdentity.Recognition.Other)
                continue;

            Repair(client);
            if (recognized != WindowIdentity.Recognition.SameClient)
                continue;

            // Nothing minimizes under Winland, so a window that is minimized now was minimized while it was away.
            // It does not rejoin, but one that was hidden is still shown again, minimized.
            if (PInvoke.IsIconic(new HWND((nint)client.Window.Value)))
            {
                if (client.Hidden)
                    Reveal(client.Window);

                continue;
            }

            kept.Add(client);
        }

        if (kept.Count == 0)
            return;

        _core.Restore(new RecoverySnapshot(kept));
        foreach (var client in kept)
        {
            if (!client.Hidden)
                continue;

            var hwnd = new HWND((nint)client.Window.Value);
            if (PInvoke.IsWindow(hwnd))
                Reveal(client.Window);
        }

        foreach (var client in kept)
        {
            var hwnd = new HWND((nint)client.Window.Value);
            if (TryDescribe(hwnd, client.Window, out var appeared))
            {
                var outcome = _core.Handle(appeared);
                NoteManaged(client.Window, outcome, appeared);
                Apply(outcome);
            }
        }

        Apply(_core.Handle(new RestoredAdopted()));
    }

    // A previous run that died left its changes on the window. They are put back before anything reads the
    // window, so a stripped frame or a cut region is not taken as the window's own.
    private static void Repair(RecoveredClient client)
    {
        if (client.FrameStyle is uint style)
            Borderless.PutBack(client.Window, style);

        if (client.Region)
            Effects.DropLeftoverRegion(client.Window);

        if (client.Layered)
            Opacity.ReleaseAdded(client.Window);
    }

    public void RevealHidden()
    {
        LandMotions();
        var hidden = new List<WindowId>();
        foreach (var client in _core.Recovery().Clients)
        {
            if (client.Hidden)
                hidden.Add(client.Window);
        }

        lock (ShowGate)
        {
            Releasing = true;
            foreach (var window in hidden)
            {
                var hwnd = new HWND((nint)window.Value);
                if (PInvoke.IsWindow(hwnd))
                    ShowConcealed(hwnd);
            }
        }
    }

    // Says the snapshot is out of date. Building it takes the core lock, so its owner does that later, off this thread.
    internal void Publish()
    {
        if (Releasing)
            return;

        _changed?.Invoke();
    }

    /// <summary>The newest recovery snapshot, or null once the exit cleanup began.</summary>
    internal RecoverySnapshot? Capture()
    {
        if (Releasing)
            return null;

        var snapshot = Stamp(_core.Recovery());
        _published = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Shows every hidden Client and puts back every frame, effect and alpha, for a crash. It reads the last
    /// published snapshot instead of the core, so a thread that died holding the core lock cannot block it.
    /// </summary>
    internal void Salvage()
    {
        Releasing = true;
        if (_published is RecoverySnapshot snapshot)
        {
            foreach (var client in snapshot.Clients)
            {
                var hwnd = new HWND((nint)client.Window.Value);
                if (client.Hidden && PInvoke.IsWindow(hwnd))
                    ShowConcealed(hwnd);
            }
        }

        PutBackFrames();
    }

    // What this run changed on each Client's window, so a restart after a crash can put it back.
    private static RecoverySnapshot Stamp(RecoverySnapshot snapshot) =>
        Effects.Stamp(Borderless.Stamp(Opacity.Stamp(snapshot)));
}
