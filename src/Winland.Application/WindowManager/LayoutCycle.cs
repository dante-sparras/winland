using static Winland.Domain.OrdinalText;

namespace Winland.Application;

/// <summary>The <c>cyclelayout</c> dispatcher.</summary>
public sealed partial class WindowManager
{
    // dwindle, scrolling, then floating. The choice lives on the Workspace until the config is applied again.
    private static readonly string[] LayoutCycle = ["dwindle", "scrolling", "floating"];

    private Outcome CycleWorkspaceLayout(string arguments)
    {
        const string name = "cyclelayout";
        var desk = CurrentWorkspace();
        var covered = desk.Covered();
        var created = LayoutNamed(NextLayout(LayoutFactory.NameOf(desk.Layout), GoesBack(arguments)));
        AdoptLayout(desk, created);
        Refit(desk);
        return _workspaces.Shown.IsShown(desk.Id)
            ? Present(desk, covered, desk.Layout.Visible(), dispatcher: name)
            : View([], dispatcher: name);
    }

    private static bool GoesBack(string arguments)
    {
        var word = arguments.Trim();
        var space = word.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0)
            word = word[..space];

        return Is(word, "back");
    }

    private static string NextLayout(string current, bool back)
    {
        var index = -1;
        for (var i = 0; i < LayoutCycle.Length; i++)
        {
            if (Is(LayoutCycle[i], current))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return "dwindle";

        var step = back ? -1 : 1;
        return LayoutCycle[((index + step) % LayoutCycle.Length + LayoutCycle.Length) % LayoutCycle.Length];
    }

    private static Layout LayoutNamed(string name) => name switch
    {
        "scrolling" => new ScrollingLayout(),
        "floating" => new FloatingLayout(),
        _ => new DwindleLayout(),
    };
}
