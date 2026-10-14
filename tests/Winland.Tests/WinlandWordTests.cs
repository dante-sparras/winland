using FluentAssertions;
using Winland.Cli;

namespace Winland.Tests;

[TestClass]
public sealed class WinlandWordTests
{
    [TestMethod]
    public void Workspaces_clients_and_the_active_window_can_ask_for_json()
    {
        var first = WinlandWords.Read(["-j", "workspaces"]);
        first.Error.Should().BeEmpty();
        first.Word.Should().Be(WinlandWord.Workspaces);
        first.Json.Should().BeTrue();

        var second = WinlandWords.Read(["clients", "--json"]);
        second.Word.Should().Be(WinlandWord.Clients);
        second.Json.Should().BeTrue();

        var third = WinlandWords.Read(["activewindow"]);
        third.Word.Should().Be(WinlandWord.ActiveWindow);
        third.Json.Should().BeFalse();
    }

    [TestMethod]
    public void Dispatch_keeps_the_command_and_its_arguments()
    {
        var jump = WinlandWords.Read(["dispatch", "workspace.jump.3"]);
        jump.Error.Should().BeEmpty();
        jump.Word.Should().Be(WinlandWord.Dispatch);
        jump.Command.Should().Be("workspace.jump.3");
        jump.Args.Should().BeEmpty();

        var focus = WinlandWords.Read(["-j", "dispatch", "focus-app", "Code"]);
        focus.Json.Should().BeTrue();
        focus.Command.Should().Be("focus-app");
        focus.Args.Should().Equal("Code");
    }

    [TestMethod]
    public void A_missing_word_an_unknown_word_and_a_bare_dispatch_are_refused()
    {
        WinlandWords.Read([]).Error.Should().Contain("winland workspaces");
        WinlandWords.Read(["nope"]).Error.Should().Be("unknown command nope");
        WinlandWords.Read(["dispatch"]).Error.Should().Be("dispatch needs a command.");
        WinlandWords.Read(["workspaces", "more"]).Error.Should().Be("workspaces takes no arguments.");
    }

    [TestMethod]
    public void Help_prints_the_words()
    {
        var help = WinlandWords.Read(["help"]);
        help.Word.Should().Be(WinlandWord.Help);
        help.Error.Should().BeEmpty();
        WinlandWords.Usage.Should().Contain("dispatch");
    }
}
