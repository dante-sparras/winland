using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>Builds a configured core, feeds it windows and keys, and reads what a step returned. Every test file sees these through a global using.</summary>
internal static class TestOutcomes
{
    /// <summary>The Client of <paramref name="window"/> in the outcome's State. Fails unless there is exactly one.</summary>
    public static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    /// <summary>Where the outcome places <paramref name="window"/>. Fails unless it places it exactly once.</summary>
    public static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    /// <summary>Puts a floating window at <paramref name="bounds"/>, as the system does when the user drags it there.</summary>
    public static Outcome Place(WindowManager core, long window, Rectangle bounds)
    {
        var id = new WindowId(window);
        var pointer = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        core.Handle(new WindowMoveStarted(id, pointer));
        return core.Handle(new WindowMoveEnded(id, pointer, bounds));
    }

    /// <summary>A JSON reply, parsed.</summary>
    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>A core that loaded <paramref name="text"/> as its config, as the daemon does at start.</summary>
    public static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    /// <summary>A core given <paramref name="text"/> through <see cref="Configure"/>.</summary>
    public static WindowManager Configured(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    /// <summary>The work area a window appears on when a test names none: a 1920x1080 Monitor at the origin.</summary>
    private static readonly Rectangle FullHd = new(0, 0, 1920, 1080);

    /// <summary>Config with no gaps and no borders, so a window fills exactly the space its Layout gives it.</summary>
    public static string FlatConfig(string extra = "") =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    /// <summary>A core given <see cref="FlatConfig"/> and <paramref name="extra"/> through <see cref="Configure"/>.</summary>
    public static WindowManager Flat(string extra = "") => Configured(FlatConfig(extra));

    /// <summary>A core with two 1920x1080 Monitors side by side, each showing one window: 1 on the left and 2 on the right.</summary>
    public static WindowManager TwoMonitors(string? config = null)
    {
        var core = config is null ? new WindowManager() : new WindowManager(ConfigParser.Parse(config));
        var left = Display(@"\\.\DISPLAY1", "Dell", FullHd);
        var right = Display(@"\\.\DISPLAY2", "Samsung", new Rectangle(1920, 0, 1920, 1080));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }

    /// <summary>A window appearing on <paramref name="area"/>, a 1920x1080 Monitor unless the test names one.</summary>
    public static WindowAppeared Appeared(
        long window,
        Rectangle? area = null,
        Point? pointer = null,
        Rectangle bounds = default,
        string? windowClass = null) =>
        new(new WindowId(window), area ?? FullHd)
        {
            Pointer = pointer,
            Bounds = bounds,
            Traits = windowClass is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = windowClass },
        };

    /// <summary>Opens <paramref name="window"/> on <paramref name="area"/>, a 1920x1080 Monitor unless the test names one.</summary>
    public static Outcome Appear(WindowManager core, long window, Rectangle? area = null) =>
        core.Handle(Appeared(window, area));

    /// <summary>Presses a key and holds it.</summary>
    public static Outcome Press(WindowManager core, string key, int scanCode = 0) =>
        core.Handle(new KeyPressed(key) { ScanCode = scanCode });

    /// <summary>Lets go of a key.</summary>
    public static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));

    /// <summary>Presses a key and lets it go, and returns what the press did.</summary>
    public static Outcome Tap(WindowManager core, string key)
    {
        var outcome = Press(core, key);
        Release(core, key);
        return outcome;
    }
}
