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
            windowrule = float, class:^(stick)$
            windowrule = pin, class:^(stick)$
            windowrule = opacity 0.8, class:^(stick)$
            windowrule = no_close_for 60000, class:^(stick)$
            windowrule = group deny, class:^(stick)$
            windowrule = no_initial_focus, class:^(quiet)$
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
        core.Handle(new WindowAppeared(new WindowId(6), Area));
        core.Handle(new WindowAppeared(new WindowId(7), Area) { Owner = new WindowId(6) });
        core.Handle(new WindowEnabledChanged(new WindowId(6), Enabled: false));
        core.Handle(new WindowAppeared(new WindowId(8), Area));
        core.Handle(new WindowAppeared(new WindowId(10), Area));
        core.Handle(new WindowAppeared(new WindowId(11), Area));
        core.Handle(new Dispatch("focuswindow", "address:0xa"));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new Dispatch("focuswindow", "address:0xb"));
        core.Handle(new Dispatch("moveintogroup", "u"));
        core.Handle(new WindowAppeared(new WindowId(12), Area));
        core.Handle(new WindowAppeared(new WindowId(13), Area) { ShownMaximized = true });
        core.Handle(new WindowTitleChanged(new WindowId(13), "renamed"));
        core.Handle(new Dispatch("focuswindow", "address:0xc"));
        core.Handle(new Dispatch("movetoworkspace", "special:magic"));
        core.Handle(new WindowFlashed(new WindowId(3)));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new Dispatch("workspace", "1"));
        core.Handle(new Dispatch("focuswindow", "address:0x8"));
        core.Handle(new Dispatch("fullscreen", "0"));
        core.Handle(new PointerMoved(new Point(100, 100)) { TimeMs = 1_000 });
        core.Handle(new PointerMoved(new Point(1800, 900)) { TimeMs = 1_100 });
        core.Handle(new WindowMoveStarted(new WindowId(1), new Point(100, 100)));
        core.Handle(new WindowFocused(new WindowId(77), FocusCause.User));

        // Window 7, the dialog of the closed owner 6, stays open.
        WindowId[] closed = [new(1), new(2), new(3), new(5), new(4), new(6), new(8), new(10), new(11), new(12), new(13), new(9), new(77)];
        var held = Leftovers(core, closed);
        var empty = Stores.Where(store => !held.Any(path => path.StartsWith(store, StringComparison.Ordinal))).ToList();
        Assert.True(empty.Count == 0, "The scenario never put a closed window in " + string.Join(", ", empty) + ". It held: " + string.Join(", ", held));

        foreach (var window in closed)
            core.Handle(new WindowClosed(window));

        var kept = Leftovers(core, closed);
        Assert.True(kept.Count == 0, "Per-window state kept a closed window: " + string.Join(", ", kept));
    }

    // What the scenario must fill with a closed window before it closes, so the walk proves something. _renamed is
    // left out: the next rule pass empties it.
    private static readonly string[] Stores =
    [
        "_props._entries", "_awaitingMapFocus", "_urgent", "_focus._history", "_focus.Unmanaged", "_restored", "_pins", "_stacking",
        "_cover._flags", "_interaction", "_owners", "_disabled", "_shownMaximized", "_lastMouse", "_mapPlaced",
        "_workspaces._all[].Focus", "_workspaces._all[]._groups", "_workspaces._all[]._waiting",
    ];

    // Every place in the core, however deep in a Winland-owned object, that holds one of the windows.
    private static List<string> Leftovers(WindowManager core, WindowId[] closed)
    {
        var kept = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk("core", core, closed, seen, kept);
        return [.. kept.Select(path => path["core.".Length..])];
    }

    private static void Walk(string path, object? value, WindowId[] closed, HashSet<object> seen, List<string> kept)
    {
        if (value is null or string or Delegate or Type)
            return;

        if (value is WindowId window)
        {
            if (closed.Contains(window))
                kept.Add($"{path} holds {window.Value}");

            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || (!type.IsValueType && !seen.Add(value)))
            return;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                Walk($"{path}.Keys", entry.Key, closed, seen, kept);
                Walk($"{path}[{(entry.Key is WindowId id ? id.Value : entry.Key)}]", entry.Value, closed, seen, kept);
            }

            return;
        }

        if (!IsWinland(type) && !type.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal))
        {
            if (value is IEnumerable items)
            {
                foreach (var item in items)
                    Walk($"{path}[]", item, closed, seen, kept);
            }

            return;
        }

        for (var owner = type; owner is not null && (IsWinland(owner) || owner == type); owner = owner.BaseType)
        {
            foreach (var field in owner.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;
                Walk($"{path}.{name}", field.GetValue(value), closed, seen, kept);
            }
        }
    }

    private static bool IsWinland(Type type) => type.Assembly.GetName().Name!.StartsWith("Winland", StringComparison.Ordinal);
}
