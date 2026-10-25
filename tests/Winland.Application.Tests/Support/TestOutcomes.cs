using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>Reads what a step returned, and builds a configured core. Every test file sees these through a global using.</summary>
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
}
