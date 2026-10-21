
namespace Winland.Application.Tests;

/// <summary>The text Winland answers clients, activewindow, workspaces, activeworkspace and monitors with without j/.</summary>
public class TextRepliesTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ClientsAndActiveWindowAnswerInThePinnedReleasesTextForm()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), Area) { Traits = WindowTraits.Normal with { Class = "term", Title = "shell" } });

        var clients = core.Execute("clients").Text;

        Assert.StartsWith("Window 2a -> shell:\n\tmapped: 1\n\thidden: 0\n\tvisible: 1\n\tacceptsInput: 1\n", clients, StringComparison.Ordinal);
        Assert.Contains("\n\tworkspace: 1 (1) on monitor \n\tfloating: 0\n\tmonitor: 0\n\tclass: term\n\ttitle: shell\n", clients, StringComparison.Ordinal);
        Assert.Contains("\n\tgrouped: 0\n\ttags: \n", clients, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", clients, StringComparison.Ordinal);
        Assert.Equal(clients, core.Execute("activewindow").Text);
        Assert.StartsWith("[", core.Execute("j/clients").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspacesAndActiveWorkspaceAnswerInText()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var workspaces = core.Execute("workspaces").Text;

        Assert.StartsWith("workspace 1 (1) on monitor ", workspaces, StringComparison.Ordinal);
        Assert.Contains("\n\twindows: 1\n\thasfullscreen: 0\n\tlastwindow: 0x1\n", workspaces, StringComparison.Ordinal);
        Assert.Equal(workspaces, core.Execute("activeworkspace").Text);
    }

    [Fact]
    public void MonitorsAnswerInText()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area)]));

        var monitors = core.Execute("monitors").Text;

        Assert.StartsWith("Monitor " + @"\\.\DISPLAY1" + " (ID 0):\n\t1920x1080@", monitors, StringComparison.Ordinal);
        Assert.Contains("\n\tfocused: yes\n\tdpmsStatus: 1\n\tvrr: false\n", monitors, StringComparison.Ordinal);
        Assert.Contains("\n\tsolitaryBlockedBy: null\n", monitors, StringComparison.Ordinal);
        Assert.EndsWith("\t\tvrrCapable: no\n\n", monitors, StringComparison.Ordinal);
    }
}
