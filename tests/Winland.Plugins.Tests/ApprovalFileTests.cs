using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

public class ApprovalFileTests
{
    private static readonly string Hash = new('a', 64);

    // The approvals file is read at logon. A null entry or an unreadable file must not end Winland.
    [Fact]
    public void NullEntriesAreSkipped()
    {
        var path = Temporary($$"""[null, {"name":"sample","sha256":"{{Hash}}"}, null]""");
        try
        {
            var log = new ListLog();

            var host = ApprovalFile.Read(path, log);
            var manager = InstalledPlugins.Read(path);

            Assert.Equal([new PluginApproval("sample", Hash)], host);
            Assert.Equal("sample", Assert.Single(manager).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileThatCannotBeReadIsReportedNotThrown()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Temporary("[]");
        using var identity = WindowsIdentity.GetCurrent();
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        var deny = new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny);
        security.AddAccessRule(deny);
        file.SetAccessControl(security);
        try
        {
            var log = new ListLog();

            var approvals = ApprovalFile.Read(path, log);

            Assert.Empty(approvals);
            Assert.Contains(log.Lines, line => line.Contains("Could not read approvals", StringComparison.Ordinal));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            file.SetAccessControl(security);
            File.Delete(path);
        }
    }

    private static string Temporary(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "winland-approvals-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class ListLog : IPluginLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string message) => Lines.Add(message);
    }
}
