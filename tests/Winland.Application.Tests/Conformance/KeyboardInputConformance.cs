namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Winland's keyboard input cases. The reference records the key events that reach the focused window;
/// here that is every key event no bind consumed. A command a bind runs shows up as a Launch.
/// </summary>
[Trait("Category", "Conformance")]
public class KeyboardInputConformance
{
    private const string Chord = "chord";

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
