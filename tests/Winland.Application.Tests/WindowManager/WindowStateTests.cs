using System.Collections;
using System.Reflection;

namespace Winland.Application.Tests;

/// <summary>Closing a window forgets it in every piece of per-window state the core keeps.</summary>
public class WindowStateTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ClosingAWindowLeavesNoPerWindowStateBehind()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            input:mouse_refocus = false
            cursor:warp_on_change_workspace = 1
            cursor:persistent_warps = true
            windowrule = float, class:^(stick)$
            windowrule = pin, class:^(stick)$
            windowrule = opacity 0.8, class:^(stick)$
            windowrule = no_close_for 60000, class:^(stick)$
            windowrule = group deny, class:^(stick)$
            windowrule = noinitialfocus, class:^(quiet)$
            """));
        core.Restore(new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(9), new WorkspaceId("4"), MonitorId.Primary, true, "gone", "", 0),
        ]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Executable = @"C:\apps\stick.exe",
            Traits = WindowTraits.Normal with { Class = "stick" },
        });
        core.Handle(new WindowAppeared(new WindowId(3), Area) { Traits = WindowTraits.Normal with { Class = "quiet" } });
        core.Handle(new WindowAppeared(new WindowId(4), Area) { ProcessId = 100, Traits = WindowTraits.Normal with { Class = "kitty" } });
        core.Handle(new WindowAppeared(new WindowId(5), Area)
        {
            ProcessId = 205,
            AncestorProcessIds = [100],
            Traits = WindowTraits.Normal with { Class = "mpv" },
        });
        core.Handle(new PointerMoved(new Point(100, 100)) { TimeMs = 1_000 });
        core.Handle(new PointerMoved(new Point(1800, 900)) { TimeMs = 1_100 });
        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        core.Handle(new WindowFlashed(new WindowId(3)));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new Dispatch("workspace", "1"));
        core.Handle(new WindowMoveStarted(new WindowId(1), new Point(100, 100)));
        core.Handle(new WindowFocused(new WindowId(77), FocusCause.User));

        WindowId[] closed = [new(1), new(2), new(3), new(5), new(4), new(9), new(77)];
        foreach (var window in closed)
            core.Handle(new WindowClosed(window));

        var kept = Leftovers(core, closed);
        Assert.True(kept.Count == 0, "Per-window state kept a closed window: " + string.Join(", ", kept));
    }

    // Every field of the core that is a collection keyed by window, or names one window, except
    // _active, which Close moves to the next Client.
    private static List<string> Leftovers(WindowManager core, WindowId[] closed)
    {
        var kept = new List<string>();
        foreach (var field in typeof(WindowManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.Name == "_active")
                continue;

            var value = field.GetValue(core);
            foreach (var window in Windows(field.FieldType, value))
            {
                if (closed.Contains(window))
                    kept.Add($"{field.Name} holds {window.Value}");
            }
        }

        return kept;
    }

    private static IEnumerable<WindowId> Windows(Type type, object? value)
    {
        if (value is null)
            yield break;

        if (type == typeof(WindowId?) && value is WindowId single)
        {
            yield return single;
            yield break;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(WindowId))
        {
            foreach (var key in ((IDictionary)value).Keys)
                yield return (WindowId)key;

            yield break;
        }

        if (value is IEnumerable<WindowId> windows)
        {
            foreach (var window in windows)
                yield return window;
        }
    }
}
