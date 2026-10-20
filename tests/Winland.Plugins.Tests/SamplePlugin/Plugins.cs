using System.Globalization;
using Winland.PluginApi;

namespace Winland.SamplePlugin;

public sealed class SamplePlugin : IPlugin
{
    public string Name => "sample";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
        host.RegisterDispatcher("sampleecho", arguments => PluginResult.Success(arguments));
        host.RegisterKeyword("samplekeyword", (value, warnings) =>
        {
            if (value == "bad")
                warnings.Warn("sample keyword rejected 'bad'.");
        });
        host.Subscribe(static _ => { });
    }

    public void Shutdown()
    {
    }
}

public sealed class FaultyPlugin : IPlugin
{
    public string Name => "faulty";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) =>
        host.RegisterDispatcher("boom", static _ => throw new InvalidOperationException("boom"));

    public void Shutdown()
    {
    }
}

public sealed class HangingPlugin : IPlugin
{
    public string Name => "hanger";

    public int ApiVersion => PluginApiVersion.Current;

    // The argument names an event the test sets once the host has answered, so the call cannot
    // finish before the host abandons it, however slow the machine is. Without one it sleeps 5 s.
    public void Initialize(IPluginHost host) =>
        host.RegisterDispatcher("hang", static arguments =>
        {
            if (arguments.Length > 0 && OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(arguments, out var release))
            {
                using (release)
                    release.WaitOne(TimeSpan.FromMinutes(1));
            }
            else
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }

            return PluginResult.Success();
        });

    public void Shutdown()
    {
    }
}

public sealed class BadVersionPlugin : IPlugin
{
    public string Name => "badversion";

    public int ApiVersion => throw new InvalidOperationException("no version");

    public void Initialize(IPluginHost host) =>
        throw new InvalidOperationException("badversion should not initialize");

    public void Shutdown()
    {
    }
}

// Every load constructs every plugin type in the assembly, so this one is slow only when a test asks.
public sealed class SlowStartPlugin : IPlugin
{
    public const string Variable = "WINLAND_SAMPLE_SLOW_START";

    public SlowStartPlugin()
    {
        if (Environment.GetEnvironmentVariable(Variable) == "1")
            Thread.Sleep(TimeSpan.FromSeconds(5));
    }

    public string Name => "slowstart";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
    }

    public void Shutdown()
    {
    }
}

public sealed class MismatchPlugin : IPlugin
{
    public string Name => "mismatch";

    public int ApiVersion => PluginApiVersion.Current + 1;

    public void Initialize(IPluginHost host) =>
        throw new InvalidOperationException("mismatch should not initialize");

    public void Shutdown()
    {
    }
}

/// <summary>Stacks tiles in rows, or in columns after <c>layoutmsg cols</c>.</summary>
public sealed class RowsPlugin : IPlugin
{
    public string Name => "rows";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) => host.RegisterLayout("rows", static () => new BandLayout());

    public void Shutdown()
    {
    }
}

/// <summary>Tries the names a plugin may not register, and reports each reason from <c>reasons</c>.</summary>
public sealed class ReservedLayoutPlugin : IPlugin
{
    public string Name => "reserved";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
        var dwindle = host.RegisterLayout("dwindle", static () => new BandLayout());
        var titled = host.RegisterLayout("Dwindle", static () => new BandLayout());
        var floating = host.RegisterLayout("default", static () => new BandLayout());
        var blank = host.RegisterLayout(" ", static () => new BandLayout());
        var lua = host.RegisterLayout("lua:rows", static () => new BandLayout());
        var first = host.RegisterLayout("probe", static () => new BandLayout());
        var second = host.RegisterLayout("probe", static () => new BandLayout());
        host.RegisterDispatcher("reasons", _ => PluginResult.Success(string.Join('\n',
            dwindle.Reason,
            titled.Reason,
            floating.Reason,
            blank.Reason,
            lua.Reason,
            first.Supported ? "probe" : first.Reason,
            second.Reason)));
    }

    public void Shutdown()
    {
    }
}

/// <summary>Registers <c>rows</c> again, which fails while <see cref="RowsPlugin"/> holds that name.</summary>
public sealed class DuplicateLayoutPlugin : IPlugin
{
    public string Name => "again";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
        var layout = host.RegisterLayout("rows", static () => new BandLayout());
        if (!layout.Supported)
            throw new InvalidOperationException(layout.Reason);
    }

    public void Shutdown()
    {
    }
}

/// <summary>Counts each layout call in <see cref="Calls"/> and then throws.</summary>
public sealed class FaultyLayoutPlugin : IPlugin
{
    public const string Calls = "WINLAND_LAYOUT_CALLS";

    public string Name => "faultylayout";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) => host.RegisterLayout("faulty", static () => new FaultyLayout());

    public void Shutdown()
    {
    }
}

/// <summary>Waits on the event named by <see cref="Variable"/> inside the layout call.</summary>
public sealed class HangingLayoutPlugin : IPlugin
{
    public const string Variable = "WINLAND_LAYOUT_HANG";

    public string Name => "hanglayout";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) => host.RegisterLayout("hang", static () => new HangingLayout());

    public void Shutdown()
    {
    }
}

internal sealed class BandLayout : PluginLayout
{
    private readonly Dictionary<long, byte> _live = [];

    private bool _columns;

    public override void WindowAdded(PluginLayoutWindow window) => _live[window.Address] = 0;

    public override void WindowRemoved(PluginLayoutWindow window) => _live.Remove(window.Address);

    public override IReadOnlyList<PluginLayoutBox>? Recalculate(PluginLayoutRequest request)
    {
        if (_live.Count != request.Windows.Count)
            return null;

        foreach (var window in request.Windows)
        {
            if (!_live.ContainsKey(window.Address))
                return null;
        }

        var count = request.Windows.Count;
        var boxes = new PluginLayoutBox[count];
        var area = request.Area;
        for (var i = 0; i < count; i++)
        {
            boxes[i] = _columns
                ? new PluginLayoutBox(area.X + (area.Width * i / count), area.Y, area.Width / count, area.Height)
                : new PluginLayoutBox(area.X, area.Y + (area.Height * i / count), area.Width, area.Height / count);
        }

        return boxes;
    }

    public override string? LayoutMessage(PluginLayoutRequest request, string message)
    {
        _ = request;
        if (message == "cols")
        {
            _columns = true;
            return null;
        }

        return message == "bad" ? "rejected" : null;
    }
}

internal sealed class FaultyLayout : PluginLayout
{
    public override IReadOnlyList<PluginLayoutBox>? Recalculate(PluginLayoutRequest request)
    {
        _ = request;
        var count = int.TryParse(Environment.GetEnvironmentVariable(FaultyLayoutPlugin.Calls), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        Environment.SetEnvironmentVariable(FaultyLayoutPlugin.Calls, (count + 1).ToString(CultureInfo.InvariantCulture));
        throw new InvalidOperationException("layout failed");
    }
}

internal sealed class HangingLayout : PluginLayout
{
    public override IReadOnlyList<PluginLayoutBox>? Recalculate(PluginLayoutRequest request)
    {
        _ = request;
        var name = Environment.GetEnvironmentVariable(HangingLayoutPlugin.Variable);
        if (!string.IsNullOrEmpty(name) && OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(name, out var release))
        {
            using (release)
                release.WaitOne(TimeSpan.FromMinutes(1));
        }
        else
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
        }

        return [];
    }
}
