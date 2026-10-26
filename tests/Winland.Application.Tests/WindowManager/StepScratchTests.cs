using System.Reflection;

namespace Winland.Application.Tests;

public class StepScratchTests
{
    private sealed record Unhandled : Input;

    [Fact]
    public void AStepThatThrowsLeavesNothingForTheNextStep()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\n"));
        var scratch = new Dictionary<string, object>
        {
            ["_configEvents"] = new List<StreamEvent> { new ClientClosed(new WindowId(1)) },
            ["_extraEvents"] = new List<StreamEvent> { new ClientClosed(new WindowId(1)) },
            ["_noted"] = new List<StreamEvent> { new ClientClosed(new WindowId(1)) },
            ["_activated"] = new List<WorkspaceId> { new("2") },
            ["_readdressed"] = new List<(WorkspaceId, WorkspaceId)> { (new WorkspaceId("2"), new WorkspaceId("3")) },
            ["_crossMonitorWarp"] = new WarpTo(new Point(5, 5)),
            ["_movedItself"] = new WindowId(1),
        };

        foreach (var (name, value) in scratch)
            Field(name).SetValue(core, value);

        Assert.Throws<ArgumentOutOfRangeException>(() => core.Handle(new Unhandled()));

        foreach (var name in scratch.Keys)
            Assert.Null(Field(name).GetValue(core));
    }

    private static FieldInfo Field(string name) =>
        typeof(WindowManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
