using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's keyboard input cases. The reference records the key events that reach the focused window;
/// here that is every key event no bind consumed. A command a bind runs shows up as a Launch.
/// </summary>
[Trait("Category", "Conformance")]
public class KeyboardInputConformance
{
    private const string Chord = "chord";

    [Fact]
    public void OrderedChordConsumption()
    {
        var desk = new Desk(KeybindsConformance.ConfigBinds);
        var keys = new Recorder(desk);
        Assert.Equal("ok", desk.Send($"keyword binds , Tab&T, exec, {Chord}"));
        keys.Register();

        // The prefix is consumed even when the chord is not completed.
        keys.Key(true, "Tab");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect();
        keys.Key(false, "Tab");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect();

        // An unrelated key is forwarded while the prefix is held, without running the bind.
        keys.Key(true, "Tab");
        keys.Key(true, "X");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect(("X", true));
        keys.Key(false, "X");
        keys.Key(false, "Tab");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect(("X", true), ("X", false));
        keys.Register();

        // A trailing key on its own is forwarded.
        keys.Key(true, "T");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect(("T", true));
        keys.Key(false, "T");
        keys.Expect(("T", true), ("T", false));

        // Completing the chord runs it once and consumes the final press.
        keys.Key(true, "Tab");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(true, "T");
        Assert.Equal(1, keys.Count(Chord));
        keys.Expect(("T", true), ("T", false));
        keys.Key(false, "T");
        keys.Key(false, "Tab");
        Assert.Equal(1, keys.Count(Chord));
        keys.Expect(("T", true), ("T", false));
    }

    [Fact]
    public void OrderedChordRejectsReorderedPrefix()
    {
        var desk = new Desk(KeybindsConformance.ConfigBinds);
        var keys = new Recorder(desk);
        Assert.Equal("ok", desk.Send($"keyword binds , A&B&C, exec, {Chord}"));

        // Holding every key is not enough: the prefix must be pressed in order.
        keys.Key(true, "B");
        keys.Key(true, "A");
        keys.Key(true, "C");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(false, "C");
        keys.Key(false, "A");
        keys.Key(false, "B");
        Assert.Equal(0, keys.Count(Chord));

        // A fresh chord in order still works after the rejected one.
        keys.Key(true, "A");
        keys.Key(true, "B");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(true, "C");
        Assert.Equal(1, keys.Count(Chord));
        keys.Key(false, "C");
        keys.Key(false, "B");
        keys.Key(false, "A");
        Assert.Equal(1, keys.Count(Chord));
    }

    [Fact]
    public void OrderedReleaseChord()
    {
        var desk = new Desk(KeybindsConformance.ConfigBinds);
        var keys = new Recorder(desk);
        Assert.Equal("ok", desk.Send($"keyword bindsr , A&B&C, exec, {Chord}"));

        // A prefix out of order does not arm the release bind.
        keys.Key(true, "B");
        keys.Key(true, "A");
        keys.Key(true, "C");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(false, "A");
        keys.Key(false, "B");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(false, "C");
        Assert.Equal(0, keys.Count(Chord));

        // Releasing the prefix first keeps a release bind armed in order.
        keys.Key(true, "A");
        keys.Key(true, "B");
        keys.Key(true, "C");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(false, "A");
        keys.Key(false, "B");
        Assert.Equal(0, keys.Count(Chord));
        keys.Key(false, "C");
        Assert.Equal(1, keys.Count(Chord));
    }

    [Fact]
    public void NonConsumingChordPreservesOrder()
    {
        var desk = new Desk(KeybindsConformance.ConfigBinds);
        var keys = new Recorder(desk);
        Assert.Equal("ok", desk.Send($"keyword bindsn , A&B&C, exec, {Chord}"));
        keys.Register();

        keys.Key(true, "B");
        keys.Key(true, "A");
        keys.Key(true, "C");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect(("B", true), ("A", true), ("C", true));
        keys.Key(false, "C");
        keys.Key(false, "A");
        keys.Key(false, "B");
        Assert.Equal(0, keys.Count(Chord));

        keys.Expect(("B", true), ("A", true), ("C", true), ("C", false), ("A", false), ("B", false));

        keys.Register();
        keys.Key(true, "A");
        keys.Expect(("A", true));
        keys.Key(true, "B");
        Assert.Equal(0, keys.Count(Chord));
        keys.Expect(("A", true), ("B", true));
        keys.Key(true, "C");
        Assert.Equal(1, keys.Count(Chord));
        keys.Expect(("A", true), ("B", true), ("C", true));
        keys.Key(false, "C");
        keys.Key(false, "B");
        keys.Key(false, "A");
        Assert.Equal(1, keys.Count(Chord));
        keys.Expect(("A", true), ("B", true), ("C", true), ("C", false), ("B", false), ("A", false));
    }

    /// <summary>
    /// The reference test plugin's keyboard and its event recorder. A key event no bind consumed reaches the focused
    /// window, so the recorder lists it. <see cref="Register"/> starts the list again, as the reference does.
    /// </summary>
    private sealed class Recorder(Desk desk)
    {
        private readonly List<(string Key, bool Pressed)> _forwarded = [];
        private readonly List<string> _launches = [];

        internal void Register() => _forwarded.Clear();

        internal void Key(bool pressed, string key)
        {
            var outcome = desk.Handle(pressed ? new KeyPressed(key) : new KeyReleased(key));
            foreach (var launch in outcome.Launches)
                _launches.Add(launch.CommandLine);
            if (!outcome.Consumed)
                _forwarded.Add((key, pressed));
        }

        internal int Count(string command) => _launches.Count(line => line == command);

        internal void Expect(params (string Key, bool Pressed)[] events) => Assert.Equal(events, _forwarded);
    }
}
