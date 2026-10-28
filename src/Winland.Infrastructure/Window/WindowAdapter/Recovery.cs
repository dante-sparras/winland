using Windows.Win32.Foundation;

namespace Winland.Infrastructure;

/// <summary>Putting windows back after a crash, and saving what this run changed on each window so the next run can.</summary>
public sealed partial class WindowAdapter : IDisposable
{
    // Stryker disable once all : matches the snapshot to the real processes and windows open now.
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

            // A clean exit showed every hidden Client. One hidden again was hidden by its program, and stays so.
            if (snapshot.Clean && client.Hidden && !_desktop.Shown(client.Window))
                continue;

            // Nothing minimizes under Winland, so a window that is minimized now was minimized while it was away.
            // It does not rejoin, but one that was hidden is still shown again, minimized.
            if (_desktop.Minimized(client.Window))
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

            if (_desktop.Exists(client.Window))
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
    private void Repair(RecoveredClient client)
    {
        if (client.FrameStyle is uint style)
            _borderless.PutBack(client.Window, style);

        if (client.Region)
            _effects.DropLeftoverRegion(client.Window);

        if (client.Layered)
            _opacity.ReleaseAdded(client.Window);
    }

    /// <summary>
    /// Closes every Client whose window is gone. A destroy event can be lost while the session is away, as when the
    /// event queue overflows around a resume, and the Client would stay as an empty tile until the next start.
    /// </summary>
    public void DropGone()
    {
        foreach (var client in _core.Recovery().Clients)
        {
            if (!_desktop.Exists(client.Window))
                Apply(_core.Handle(new WindowClosed(client.Window)));
        }
    }

    public void RevealHidden()
    {
        // Releasing comes first, so an outcome applied on another thread meanwhile starts no motion after they landed.
        lock (_showGate)
            _releasing = true;

        LandMotions();
        var hidden = new List<WindowId>();
        foreach (var client in _core.Recovery().Clients)
        {
            if (client.Hidden)
                hidden.Add(client.Window);
        }

        lock (_showGate)
        {
            foreach (var window in hidden)
            {
                if (_desktop.Exists(window))
                    ShowConcealed(window);
            }
        }
    }

    // Says the snapshot is out of date. Building it takes the core lock, so its owner does that later, off this thread.
    internal void Publish()
    {
        if (_releasing)
            return;

        _changed?.Invoke();
    }

    /// <summary>The newest recovery snapshot, or null once the exit cleanup began.</summary>
    internal RecoverySnapshot? Capture()
    {
        if (_releasing)
            return null;

        var snapshot = Stamp(_core.Recovery());
        _published = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Shows every hidden Client and puts back every frame, effect and alpha, for a crash. It reads what this
    /// adapter hid and the last published snapshot instead of the core, so a thread that died holding the core
    /// lock cannot block it. The snapshot can be a quarter second old, and a window hidden since is in Concealed.
    /// </summary>
    internal void Salvage()
    {
        _releasing = true;
        var hidden = new HashSet<WindowId>(_concealed.Keys);
        if (_published is RecoverySnapshot snapshot)
        {
            foreach (var client in snapshot.Clients)
            {
                if (client.Hidden)
                    hidden.Add(client.Window);
            }
        }

        foreach (var window in hidden)
        {
            if (_desktop.Exists(window))
                ShowConcealed(window);
        }

        PutBackFrames();
    }

    // What this run changed on each Client's window, so a restart after a crash can put it back. After the exit cleanup,
    // what it could not put back, on a window that stopped answering.
    internal RecoverySnapshot Stamp(RecoverySnapshot snapshot) =>
        _effects.Stamp(_borderless.Stamp(_opacity.Stamp(snapshot)));
}
