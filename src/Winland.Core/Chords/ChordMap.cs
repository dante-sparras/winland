namespace Winland.Core.Chords;

/// <summary>
/// Maps Omarchy Quattro chords onto commands.
/// Print Screen opens Winoma's picker (ADR 0084). Alt+Tab is not in this map.
/// A plugin's chords come from its manifest, not from here. The Menu's are there: Super+Space, Super+Ctrl+V, Super+Escape, and the rest.
/// </summary>
public sealed class ChordMap : IChordTable
{
    private readonly Dictionary<Chord, ShellCommand> _commands = Build();

    /// <summary>The one built-in map. It never changes, so a caller that only reads it has no need of its own.</summary>
    public static ChordMap Shared { get; } = new();

    /// <summary>The built-in bindings, in insertion order.</summary>
    public IReadOnlyCollection<KeyValuePair<Chord, ShellCommand>> Bindings => _commands;

    /// <inheritdoc />
    public ShellCommand? Resolve(Chord chord) => _commands.GetValueOrDefault(chord);

    /// <inheritdoc />
    public bool IsHold(Chord chord) => false;

    /// <inheritdoc />
    public ShellCommand? ResolveHold(Chord chord) => null;

    private static Dictionary<Chord, ShellCommand> Build()
    {
        var map = new Dictionary<Chord, ShellCommand>();
        var digits = new[]
        {
            ChordKey.D1, ChordKey.D2, ChordKey.D3, ChordKey.D4, ChordKey.D5,
            ChordKey.D6, ChordKey.D7, ChordKey.D8, ChordKey.D9,
        };
        for (var i = 0; i < digits.Length; i++)
        {
            var number = i + 1;
            map[new Chord(ChordModifiers.Super, digits[i])] = new ShellCommand.JumpWorkspace(number);
            map[new Chord(ChordModifiers.Super | ChordModifiers.Shift, digits[i])] = new ShellCommand.MoveWindow(number, Follow: true);
            map[new Chord(ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Alt, digits[i])] = new ShellCommand.MoveWindow(number, Follow: false);
        }

        Add(map, ChordModifiers.Super, ChordKey.Tab, new ShellCommand.CycleWorkspace(WorkspaceCycle.Next));
        Add(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.Tab, new ShellCommand.CycleWorkspace(WorkspaceCycle.Previous));
        Add(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Tab, new ShellCommand.CycleWorkspace(WorkspaceCycle.Former));

        AddDirection(map, ChordModifiers.Super, static direction => new ShellCommand.Focus(direction));
        AddDirection(map, ChordModifiers.Super | ChordModifiers.Shift, static direction => new ShellCommand.Swap(direction));

        Window(map, ChordModifiers.Super, ChordKey.W, WindowAction.Close);
        Window(map, ChordModifiers.Super, ChordKey.Q, WindowAction.Close);
        Window(map, ChordModifiers.Super, ChordKey.T, WindowAction.ToggleFloat);
        Window(map, ChordModifiers.Super, ChordKey.F, WindowAction.ToggleFullscreen);
        Window(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.F, WindowAction.ToggleTiledFullscreen);
        Window(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.F, WindowAction.ToggleFullWidth);
        Window(map, ChordModifiers.Super, ChordKey.L, WindowAction.ToggleLayout);
        Window(map, ChordModifiers.Super, ChordKey.J, WindowAction.ToggleSplit);
        Window(map, ChordModifiers.Super, ChordKey.G, WindowAction.ToggleGroup);
        Window(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.G, WindowAction.LeaveGroup);
        Window(map, ChordModifiers.Super, ChordKey.P, WindowAction.TogglePseudo);
        Window(map, ChordModifiers.Super, ChordKey.O, WindowAction.ToggleSticky);
        Window(map, ChordModifiers.Super, ChordKey.S, WindowAction.ToggleScratchpad);
        Window(map, ChordModifiers.Super, ChordKey.Grave, WindowAction.ToggleScratchpad);
        Window(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.S, WindowAction.SendScratchpad);
        Window(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.Grave, WindowAction.SendScratchpad);
        AddDirection(map, ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Alt, static direction => new ShellCommand.MoveMonitor(direction));
        Window(map, ChordModifiers.Ctrl | ChordModifiers.Alt, ChordKey.Tab, WindowAction.NextMonitor);
        Window(map, ChordModifiers.Ctrl | ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.Tab, WindowAction.PreviousMonitor);
        Add(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.Tab, new ShellCommand.GroupStep(1));
        Add(map, ChordModifiers.Super | ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.Tab, new ShellCommand.GroupStep(-1));
        Add(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Left, new ShellCommand.GroupStep(-1));
        Add(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Right, new ShellCommand.GroupStep(1));
        for (var member = 1; member <= 5; member++)
        {
            map[new Chord(ChordModifiers.Super | ChordModifiers.Alt, digits[member - 1])] = new ShellCommand.GroupIndex(member);
        }

        AddDirection(map, ChordModifiers.Super | ChordModifiers.Alt, static direction => new ShellCommand.JoinGroup(direction));
        Window(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.Backspace, WindowAction.ToggleSpacing);
        Window(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Backspace, WindowAction.ToggleSquareTile);
        Window(map, ChordModifiers.Super | ChordModifiers.Ctrl | ChordModifiers.Alt, ChordKey.F, WindowAction.ToggleFullscreenDesktop);

        // Windows minimizes every window but the one in front on Super+Home. Bound here, the key does not reach it.
        Window(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.Home, WindowAction.SaveWidth);
        Window(map, ChordModifiers.Super, ChordKey.Home, WindowAction.RestoreWidth);
        Host(map, ChordModifiers.Super, ChordKey.Return, HostAction.Terminal);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.Return, HostAction.Browser);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Alt, ChordKey.B, HostAction.PrivateBrowser);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Space, HostAction.NextBackground);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.F, HostAction.Files);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Alt, ChordKey.F, HostAction.FilesHere);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.T, HostAction.Activity);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.L, HostAction.Lock);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.Space, HostAction.ToggleBar);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.I, HostAction.ToggleIdleLock);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Q, HostAction.Calculator);

        // Mirror and extend have no default Chord: mirror waits on Dante pressing Super+Ctrl+Alt+Delete once (ticket 19), and extend never had one.
        // Scale has no Chord at all. Super+Slash and Super+Alt+Slash stay unbound, and a monitor's scale stays in Windows Settings.
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Delete, HostAction.DisplayInternal);
        Host(map, ChordModifiers.Super, ChordKey.C, HostAction.Copy);
        Host(map, ChordModifiers.Super, ChordKey.X, HostAction.Cut);
        Host(map, ChordModifiers.Super, ChordKey.V, HostAction.Paste);
        Host(map, ChordModifiers.None, ChordKey.PrintScreen, HostAction.Capture);

        // The App chords, each on the manual's key. A role the Host names no program for starts nothing and still keeps its key
        // from Windows, which opens its own snip on Super+Shift+S and restores the windows it minimized on Super+Shift+M.
        foreach (var role in AppRoles.Rows)
        {
            Add(map, role.Chord.Modifiers, role.Chord.Key, new ShellCommand.AppRole(role.Name));
        }

        // Windows starts its screen recorder and the Game Bar's on these. Alt+Print Screen is the recorder here.
        // Bare Super+R is left unbound and opens the Run box.
        Add(map, ChordModifiers.Super | ChordModifiers.Shift, ChordKey.R, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.R, new ShellCommand.Nothing());

        // Windows acts on these, so each is bound to nothing and its key does not reach Windows.
        // In order: voice typing, minimize all, the notification center, the accessibility settings, snap layouts,
        // the emoji panel, and the Magnifier settings.
        // A change that binds one of these Chords replaces its line.
        Add(map, ChordModifiers.Super, ChordKey.H, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super, ChordKey.M, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super, ChordKey.N, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super, ChordKey.U, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super, ChordKey.Z, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super, ChordKey.Period, new ShellCommand.Nothing());
        Add(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.M, new ShellCommand.Nothing());
        Host(map, ChordModifiers.Alt, ChordKey.PrintScreen, HostAction.ScreenRecord);
        Host(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.LeftBracket, HostAction.WebcamSmaller);
        Host(map, ChordModifiers.Super | ChordModifiers.Alt, ChordKey.RightBracket, HostAction.WebcamLarger);
        Host(map, ChordModifiers.Super, ChordKey.PrintScreen, HostAction.ColorPick);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.PrintScreen, HostAction.ExtractText);

        // Windows toggles Narrator on Super+Ctrl+Return, so binding it also keeps the key from Windows.
        // Super+Ctrl+S is the Share plugin's, in its manifest.
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Return, HostAction.Herdr);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.K, HostAction.HerdrKeys);
        Host(map, ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.Period, HostAction.Transcode);
        Host(map, ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Ctrl, ChordKey.A, HostAction.Agent);
        Host(map, ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.L, HostAction.CopyPageAddress);
        Host(map, ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.D, HostAction.DownloadPageVideo);
        AddResize(map, ChordModifiers.Super, 100);
        AddResize(map, ChordModifiers.Super | ChordModifiers.Alt, 25);
        AddResize(map, ChordModifiers.Super | ChordModifiers.Ctrl, 300);
        map[new Chord(ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.A)] = new ShellCommand.OpenPanel("winoma.audio");
        map[new Chord(ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.W)] = new ShellCommand.OpenPanel("winoma.network");

        // Super+Ctrl+D is the display plugin's, in its manifest (ADR 0031).
        map[new Chord(ChordModifiers.Super | ChordModifiers.Ctrl, ChordKey.P)] = new ShellCommand.OpenPanel("winoma.power");
        map[new Chord(ChordModifiers.Super | ChordModifiers.Ctrl | ChordModifiers.Alt, ChordKey.D)] = new ShellCommand.OpenPanel("winoma.clock");
        map[new Chord(ChordModifiers.Super | ChordModifiers.Shift | ChordModifiers.Alt, ChordKey.Comma)] = new ShellCommand.OpenPanel("winoma.notifications");
        for (var panel = 1; panel <= 9; panel++)
        {
            map[new Chord(ChordModifiers.Super | ChordModifiers.Ctrl, digits[panel - 1])] = new ShellCommand.OpenRightPanel(panel);
        }

        return map;
    }

    private static void AddDirection(Dictionary<Chord, ShellCommand> map, ChordModifiers modifiers, Func<Direction, ShellCommand> command)
    {
        map[new Chord(modifiers, ChordKey.Left)] = command(Direction.Left);
        map[new Chord(modifiers, ChordKey.Right)] = command(Direction.Right);
        map[new Chord(modifiers, ChordKey.Up)] = command(Direction.Up);
        map[new Chord(modifiers, ChordKey.Down)] = command(Direction.Down);
    }

    private static void Add(Dictionary<Chord, ShellCommand> map, ChordModifiers modifiers, ChordKey key, ShellCommand command) =>
        map[new Chord(modifiers, key)] = command;

    private static void AddResize(Dictionary<Chord, ShellCommand> map, ChordModifiers modifiers, int pixels)
    {
        map[new Chord(modifiers, ChordKey.Minus)] = new ShellCommand.Resize(-pixels, 0);
        map[new Chord(modifiers, ChordKey.Equal)] = new ShellCommand.Resize(pixels, 0);
        map[new Chord(modifiers | ChordModifiers.Shift, ChordKey.Minus)] = new ShellCommand.Resize(0, -pixels);
        map[new Chord(modifiers | ChordModifiers.Shift, ChordKey.Equal)] = new ShellCommand.Resize(0, pixels);
    }

    private static void Window(Dictionary<Chord, ShellCommand> map, ChordModifiers modifiers, ChordKey key, WindowAction action) =>
        map[new Chord(modifiers, key)] = new ShellCommand.Window(action);

    private static void Host(Dictionary<Chord, ShellCommand> map, ChordModifiers modifiers, ChordKey key, HostAction action) =>
        map[new Chord(modifiers, key)] = new ShellCommand.Host(action);
}
