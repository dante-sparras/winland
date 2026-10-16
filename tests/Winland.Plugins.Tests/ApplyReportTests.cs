using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

[SupportedOSPlatform("windows")]
public class ApplyReportTests
{
    private const string Requester = "S-1-5-21-1-2-3-1001";
    private const string Approver = "S-1-5-21-1-2-3-500";

    [Fact]
    public void ReportTravelsOverThePipe()
    {
        using var listener = ApplyReportListener.Start();

        var sent = ApplyReport.TrySend(listener.Name, ["Installed plugin 'sample'.", "Removed plugin 'gone'."], TimeSpan.FromSeconds(5), listener.User, out var error);

        Assert.True(sent, error);
        Assert.Equal(["Installed plugin 'sample'.", "Removed plugin 'gone'."], listener.Collect());
    }

    [Fact]
    public void NoSenderCollectsNothing()
    {
        using var listener = ApplyReportListener.Start();

        Assert.Empty(listener.Collect());
    }

    [Fact]
    public void NamesAreWinlandApplyPipesOnly()
    {
        using var listener = ApplyReportListener.Start();

        Assert.True(ApplyReport.IsName(listener.Name));
        Assert.False(ApplyReport.IsName(@"C:\Users\me\apply.log"));
        Assert.False(ApplyReport.IsName(@"\\.\pipe\winland-apply-00000000000000000000000000000000"));
        Assert.False(ApplyReport.IsName("winland-apply-0000000000000000000000000000000"));
        Assert.False(ApplyReport.IsName("winland-apply-0000000000000000000000000000000G"));
        Assert.False(ApplyReport.TrySend(@"C:\Users\me\apply.log", ["x"], TimeSpan.FromSeconds(1), null, out _));
    }

    // With an over-the-shoulder approval the elevated step runs as the approving administrator,
    // and the pipe belongs to the user who asked. A pipe of anyone else is not written to.
    [Fact]
    public void TheElevatedStepWritesToItsOwnPipeOrTheRequestersOnly()
    {
        Assert.True(ApplyReport.AcceptsOwner(Requester, Requester, null));
        Assert.True(ApplyReport.AcceptsOwner(Requester, Approver, Requester));
        Assert.True(ApplyReport.AcceptsOwner(Approver, Approver, Requester));
        Assert.False(ApplyReport.AcceptsOwner("S-1-5-21-1-2-3-1002", Approver, Requester));
        Assert.False(ApplyReport.AcceptsOwner(Requester, Approver, null));
        Assert.False(ApplyReport.AcceptsOwner(null, Approver, Requester));
    }

    // A manager that is already elevated makes its pipe with Administrators as the owner. A program
    // that is not elevated cannot give a pipe that owner, so it is not a pipe someone else made.
    [Fact]
    public void ThePipeOfAnElevatedManagerIsAccepted()
    {
        Assert.True(ApplyReport.AcceptsOwner(AdministratorOnly.AdministratorsSid, Approver, Requester));
        Assert.True(ApplyReport.AcceptsOwner(AdministratorOnly.AdministratorsSid, Approver, null));
    }

    // Administrators is a deny-only group in a token that is not elevated, so the grant lets in an
    // elevated step that runs as another administrator and no program of another user.
    [Fact]
    public void ThePipeAdmitsTheUserAndAdministratorsOnly()
    {
        var user = new SecurityIdentifier(Requester);

        var rules = ApplyReportListener.SecurityFor(user)
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();

        Assert.All(rules, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
        Assert.Equal(
            [Requester, "S-1-5-32-544"],
            rules.Select(rule => rule.IdentityReference.Value).Order(StringComparer.Ordinal));
        Assert.All(rules, rule => Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.WriteData)));
    }

    // The listener names the user who owns its pipe, for the elevated step to expect.
    [Fact]
    public void TheListenerNamesTheCurrentUser()
    {
        using var listener = ApplyReportListener.Start();
        using var identity = WindowsIdentity.GetCurrent();

        Assert.Equal(identity.User?.Value, listener.User);
    }
}
