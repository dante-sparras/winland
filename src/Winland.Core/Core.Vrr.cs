namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, string> _executables = [];

    private void RememberExecutable(WindowAppeared appeared)
    {
        if (appeared.Executable.Length > 0)
            _executables[appeared.Window] = appeared.Executable;
    }

    private VrrRequest VrrNow(State state)
    {
        var suppressed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in state.Clients)
        {
            if (OwnProp(client.Window, "no_vrr") is not true)
                continue;

            if (!_executables.TryGetValue(client.Window, out var path) || path.Length == 0)
                continue;

            if (seen.Add(path))
                suppressed.Add(path);
        }

        suppressed.Sort(StringComparer.OrdinalIgnoreCase);
        return new VrrRequest(suppressed, GlobalVrr());
    }

    // 0 and 1 are a fixed flag. 2 and 3 follow a fullscreen window, which this setting cannot, and an absent option
    // is left alone.
    private bool? GlobalVrr() => _session.Vrr switch
    {
        0 => false,
        1 => true,
        _ => null,
    };
}
