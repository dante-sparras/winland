namespace Winland.Application.Tests;

/// <summary>The text Winland answers clients, activewindow, workspaces, activeworkspace, monitors, binds, version and configerrors with without j/.</summary>
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
        Assert.Contains("\n\tgrouped: 0\n\tstableID: 2a\n\n", clients, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", clients, StringComparison.Ordinal);
        Assert.Equal(clients, core.Execute("activewindow").Text);
        Assert.StartsWith("[", core.Execute("j/clients").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowTextCannotForgeLinesInQueries()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), Area)
        {
            Traits = WindowTraits.Normal with { Class = "app\rclass", Title = "title\r\nWindow 99 -> forged:\nline" },
        });

        var clients = core.Execute("clients").Text;

        Assert.StartsWith("Window 2a -> title Window 99 -> forged: line:\n", clients, StringComparison.Ordinal);
        Assert.Contains("\n\tclass: app class\n\ttitle: title Window 99 -> forged: line\n", clients, StringComparison.Ordinal);
        Assert.Contains("\n\tinitialTitle: title Window 99 -> forged: line\n", clients, StringComparison.Ordinal);
        Assert.Equal(clients, core.Execute("activewindow").Text);
        Assert.Contains("\n\tlastwindowtitle: title Window 99 -> forged: line\n", core.Execute("workspaces").Text, StringComparison.Ordinal);
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

    [Fact]
    public void BindsAnswerOneConfigLineEach()
    {
        var core = new WindowManager(ConfigParser.Parse(
            """
            bind = SUPER, W, killactive
            bind = SUPER SHIFT, 1, workspace, 1
            bindle = SUPER, minus, resizeactive, -40 0
            bindd = SUPER, Q, Close the window, killactive
            bindm = SUPER, mouse:273, resizewindow 1
            """));

        var binds = core.Execute("binds").Text;

        Assert.Equal(
            "bind = SUPER, W, killactive\n"
            + "bind = SUPER SHIFT, 1, workspace, 1\n"
            + "bindle = SUPER, minus, resizeactive, -40 0\n"
            + "bindd = SUPER, Q, Close the window, killactive\n"
            + "bindm = SUPER, mouse:273, resizewindow 1",
            binds);
        Assert.StartsWith("[{", core.Execute("j/binds").Text, StringComparison.Ordinal);
        Assert.Equal("no binds", new WindowManager().Execute("binds").Text);
        Assert.Equal("[]", new WindowManager().Execute("j/binds").Text);
    }

    [Fact]
    public void EachBindReadsBackFromItsLineAsTheSameBind()
    {
        var document = ConfigParser.Parse(
            """
            bind = SUPER, W, killactive
            bind = , Print, exec, screenshot
            bind = SUPER SHIFT CTRL ALT, Left, movewindow, mon:l
            bind = SUPER CAPS MOD2 MOD3 MOD5, F5, killactive
            bind = SUPER, 1, workspace, 1
            bind = SUPER, minus, resizeactive, -40 0
            bind = SUPER, equal, resizeactive, 40 0
            bind = SUPER, grave, togglespecialworkspace
            bind = SUPER, comma, exec, one, two
            bind = SUPER, Return, exec, wt
            bind = SUPER, code:50, killactive
            bind = SUPER, 25, killactive
            bind = SUPER, Super_L, exec, start menu
            bind = SUPER, N, exec, echo ## literal
            bind = SUPER, E, exec, [float; tile] notepad
            bind = SUPER, Z, mouse, movewindow
            bindl = SUPER, L, exec, lock
            bindr = SUPER, Super_L, exec, release
            binde = SUPER, Right, resizeactive, 20 0
            bindn = , Escape, exec, quiet
            bindle = SUPER, Up, resizeactive, 0 -20
            bindd = SUPER, Q, Close the window, killactive
            binddl = SUPER, X, Cut, sendshortcut, CTRL, X,
            bindm = SUPER, mouse:272, movewindow
            bindm = SUPER, mouse:273, resizewindow 1
            bindmd = SUPER, mouse:274, Move, movewindow
            """);
        Assert.Empty(document.Warnings);
        var binds = document.Binds.OfType<Bind>().ToArray();
        Assert.Equal(26, binds.Length);

        var text = CommandProtocol.FormatBindsText(binds);
        var again = ConfigParser.Parse(text);

        Assert.Empty(again.Warnings);
        Assert.Equal(binds, again.Binds);
    }

    [Fact]
    public void VersionAnswersOneLine()
    {
        var version = new WindowManager().Execute("version").Text;

        Assert.StartsWith("winland " + BuildStamp.Version, version, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', version);
        if (BuildStamp.Commit.Length > 0)
            Assert.Contains(BuildStamp.Commit[..Math.Min(7, BuildStamp.Commit.Length)], version, StringComparison.Ordinal);
        Assert.StartsWith("{", new WindowManager().Execute("j/version").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigErrorsAnswerOneFileLineAndMessageEach()
    {
        const string path = @"C:\winland\winland.conf";
        const string colors = @"C:\winland\colors.conf";
        var core = new WindowManager();
        Assert.Equal("no config errors", core.Execute("configerrors").Text);
        Assert.Equal("[]", core.Execute("j/configerrors").Text);

        core.Handle(new Configure(new ConfigDocument(
            [],
            [],
            [new ConfigWarning(1, "Unsupported keyword 'blur'."), new ConfigWarning(3, "Invalid modifier 'X'.", colors), new ConfigWarning(0, "Could not read config.")])
        {
            Path = path,
        }));

        Assert.Equal(
            path + ":1: Unsupported keyword 'blur'.\n" + colors + ":3: Invalid modifier 'X'.\n" + path + ": Could not read config.",
            core.Execute("configerrors").Text);
        Assert.Equal(
            "[\"line 1: Unsupported keyword 'blur'.\",\"line 3: Invalid modifier 'X'.\",\"Could not read config.\"]",
            core.Execute("j/configerrors").Text);
        Assert.Equal("ok", core.Execute("keyword general:border_size 4").Text);
        Assert.StartsWith(path + ":1:", core.Execute("configerrors").Text, StringComparison.Ordinal);
    }
}
