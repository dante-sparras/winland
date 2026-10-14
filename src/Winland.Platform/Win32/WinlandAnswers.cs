
namespace Winland.Platform.Win32;

/// <summary>The shell's answers to the window manager. <see cref="Arm"/> runs once, before a hook or a placer.</summary>
public static class WinlandAnswers
{
    /// <summary>Points Winland at this process's picker, hide ledger, gateway, and session lock.</summary>
    public static void Arm() => ShellBridge.Use(
        new GatewayProbe(),
        new LedgerHiding(),
        new PickerClaim(),
        new SessionArm(),
        ElevatedChordFiles.Open);

    private sealed class GatewayProbe : IWindowProbe
    {
        public bool IsElevated(nint hwnd) => WindowGateway.IsElevated(hwnd);

        public void FocusFromHelper(nint hwnd) => WindowGateway.FocusFromHelper(hwnd);
    }

    private sealed class LedgerHiding : IWindowHiding
    {
        public bool Showing(nint hwnd) => HiddenLedger.Showing(hwnd);

        public void Show(nint hwnd) => HiddenLedger.Windows.Show((long)hwnd);

        public void Hide(nint hwnd) => HiddenLedger.Windows.Hide((long)hwnd);
    }

    private sealed class PickerClaim : IKeyClaim
    {
        public bool Open
        {
            get => ShellState.Picker.Open;
            set => ShellState.Picker.Open = value;
        }

        public bool Take(int vk, bool down, bool super, bool ctrl, bool alt, out int? claimed)
        {
            var take = ShellState.Picker.OnKey(vk, down, super, ctrl, alt);
            claimed = take.Id;
            return take.Kept;
        }
    }

    private sealed class SessionArm : ISessionArm
    {
        public void Arm(Action release) => LockRelease.BeforeLock = release;

        public void Disarm(Action release)
        {
            if (LockRelease.BeforeLock == release)
            {
                LockRelease.BeforeLock = null;
            }
        }
    }
}

/// <summary>The elevated helper's chord file, read the way the helper read it before the hook moved.</summary>
internal static class ElevatedChordFiles
{
    /// <summary>Opens <c>binds.json</c> and watches it. The caller disposes the watch.</summary>
    /// <returns>The gate, and the watch.</returns>
    internal static (IElevatedChords Chords, IDisposable Watch) Open()
    {
        var path = Path.Combine(ConfigFolder.Path, ChordBook.FileName);
        var table = new ChordSwitch(ChordBook.Read(path));
        var watch = new ChordFileWatch(path, () => null, table.Use);
        return (new Adapter(new ElevatedChordGate(table)), watch);
    }

    private sealed class Adapter(ElevatedChordGate gate) : IElevatedChords
    {
        public GateEffect OnKey(PhysicalKey key, bool down, long now, bool desktopSawThis, bool own, DesktopSuper? desktopSuper = null) =>
            gate.OnKey(key, down, now, desktopSawThis, own, desktopSuper);

        public bool Closes(GateEffect effect) => gate.Closes(effect);

        public QueuedChord? Forwards(GateEffect effect) => gate.Forwards(effect);

        public bool DesktopSuperWentUp => gate.DesktopSuperWentUp;
    }
}
