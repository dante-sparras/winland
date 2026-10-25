namespace Winland.Infrastructure.Tests;

/// <summary>
/// A WindowManager and a WindowAdapter over a <see cref="FakeDesktop"/>, with a clock the test moves and motions it
/// steps by hand. Windows are opened at <see cref="Area"/>, the way the core is told of them.
/// </summary>
internal sealed class TestWindows : IDisposable
{
    internal static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private double _now;

    /// <param name="config">Config lines after the one that turns the borders off. A border is a window of Winland's own, which no test puts on the desktop.</param>
    /// <param name="reducedMotion">Windows move, hide and close at once, as with the system's setting.</param>
    /// <param name="changed">What the adapter calls after it applied an outcome.</param>
    internal TestWindows(string config = "", bool reducedMotion = false, Action? changed = null)
    {
        Desktop = new FakeDesktop(() => _now);
        Core = new WindowManager();
        Core.Handle(new ConfigLoaded(ConfigParser.Parse("general:border_size = 0\n" + config)));
        Adapter = new WindowAdapter(Core, Desktop, changed, () => _now, threaded: false);
        Core.ReducedMotion = () => reducedMotion;
    }

    internal WindowManager Core { get; }

    internal FakeDesktop Desktop { get; }

    internal WindowAdapter Adapter { get; }

    /// <summary>Milliseconds on the clock the adapter and the desktop read.</summary>
    internal double Now => _now;

    /// <summary>Opens a window on <see cref="Area"/> and tells the core, then applies what it answers.</summary>
    internal WindowId Open(long id)
    {
        Desktop.Add(id, Area);
        var window = new WindowId(id);
        Apply(Core.Handle(new WindowAppeared(window, Area) { Traits = WindowTraits.Normal }));
        return window;
    }

    internal Outcome Dispatch(string name, string arguments = "")
    {
        var outcome = Core.Handle(new Dispatch(name, arguments));
        Apply(outcome);
        return outcome;
    }

    internal void Apply(Outcome outcome)
    {
        Assert.All(outcome.Decorations, decoration => Assert.Null(decoration.Ring));
        Adapter.Apply(outcome);
    }

    /// <summary>Moves the clock on and steps the motions once.</summary>
    internal bool Advance(double milliseconds)
    {
        _now += milliseconds;
        return Adapter.Tick();
    }

    /// <summary>Plays the motions to their end, a frame at a time.</summary>
    internal void Settle()
    {
        for (var frame = 0; frame < 1000; frame++)
        {
            if (!Advance(16))
                return;
        }

        throw new InvalidOperationException("The motions did not end.");
    }

    public void Dispose() => Adapter.Dispose();
}
