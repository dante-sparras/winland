using System.Xml.Linq;

namespace Winland.Install.Tests;

public class LogonTaskDefinitionTests
{
    [Fact]
    public void LogonTaskRunsAtLogonWithHighestPrivilegesForTheUser()
    {
        var task = LogonTaskDefinition.Build(@"C:\Tools & Games\winland.exe", @"DESKTOP\dante");

        Assert.Equal("Winland", task.Name);
        Assert.Equal(@"C:\Tools & Games\winland.exe", task.Executable);
        Assert.Equal(@"DESKTOP\dante", task.User);

        var root = XDocument.Parse(task.Xml).Root;
        Assert.NotNull(root);
        Assert.Equal("Task", root.Name.LocalName);
        Assert.Single(root.Descendants(), element => element.Name.LocalName == "LogonTrigger");
        Assert.Single(root.Descendants(), element => element.Name.LocalName == "Exec");

        var trigger = root.Descendants().Single(element => element.Name.LocalName == "LogonTrigger");
        Assert.Equal("true", Child(trigger, "Enabled"));
        Assert.Equal(@"DESKTOP\dante", Child(trigger, "UserId"));

        var principal = root.Descendants().Single(element => element.Name.LocalName == "Principal");
        Assert.Equal(@"DESKTOP\dante", Child(principal, "UserId"));
        Assert.Equal("InteractiveToken", Child(principal, "LogonType"));
        Assert.Equal("HighestAvailable", Child(principal, "RunLevel"));

        var command = root.Descendants().Single(element => element.Name.LocalName == "Command");
        Assert.Equal(@"C:\Tools & Games\winland.exe", command.Value);

        Assert.Equal("Start Winland at logon.", Text(root, "Description"));
        Assert.Equal("IgnoreNew", Text(root, "MultipleInstancesPolicy"));
        Assert.Equal("false", Text(root, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Text(root, "StopIfGoingOnBatteries"));
        Assert.Equal("PT0S", Text(root, "ExecutionTimeLimit"));
    }

    private static string Child(XElement parent, string localName) =>
        parent.Elements().Single(element => element.Name.LocalName == localName).Value;

    private static string Text(XElement root, string localName) =>
        root.Descendants().Single(element => element.Name.LocalName == localName).Value;
}
