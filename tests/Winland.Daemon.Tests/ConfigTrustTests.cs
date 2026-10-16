using Winland;
using Winland.Plugins;

namespace Winland.Daemon.Tests;

public sealed class ConfigTrustTests : IDisposable
{
    private const string User = "S-1-5-21-9";

    private static readonly AccessList Protected = new(
        AdministratorOnly.AdministratorsSid,
        [
            new PrincipalAccess(AdministratorOnly.SystemSid, CanWrite: true, AppliesToTarget: true),
            new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
            new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true),
        ]);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "winland-trust-" + Guid.NewGuid().ToString("N"));

    public ConfigTrustTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void FilesOnlyAdministratorsCanWriteAreTrusted()
    {
        var trust = new ConfigTrust();

        trust.Note(@"C:\ProgramData\Winland\winland.conf", Protected);
        trust.Note(@"C:\ProgramData\Winland\binds.conf", Protected);

        Assert.Null(trust.Refusal);
    }

    // Every sourced file counts: one the user can write could add an elevated exec line.
    [Fact]
    public void OneFileANonAdministratorCanWriteRefusesTheWholeConfig()
    {
        var trust = new ConfigTrust();

        trust.Note(@"C:\ProgramData\Winland\winland.conf", Protected);
        trust.Note(@"C:\Users\me\binds.conf", new AccessList(User, []));
        trust.Note(@"C:\Users\me\more.conf", new AccessList(User, []));

        Assert.NotNull(trust.Refusal);
        Assert.Contains(@"C:\Users\me\binds.conf", trust.Refusal, StringComparison.Ordinal);
        Assert.Contains(User, trust.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWhoseAccessListCannotBeReadIsNotTrusted()
    {
        var trust = new ConfigTrust();

        trust.Note(@"C:\c\winland.conf", null);

        Assert.NotNull(trust.Refusal);
        Assert.Contains(@"C:\c\winland.conf", trust.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTheUserWroteIsNotTrusted()
    {
        var path = Path.Combine(_root, "winland.conf");
        File.WriteAllText(path, "exec = [elevated] cmd\n");
        var source = new FileConfigSource(_root);

        var text = source.Read(path);

        Assert.Equal("exec = [elevated] cmd\n", text);
        Assert.NotNull(source.Trust.Refusal);
        Assert.Contains(path, source.Trust.Refusal, StringComparison.Ordinal);
        Assert.Contains("non-administrator (owner S-1-5-21-", source.Trust.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotThereIsNotCounted()
    {
        var source = new FileConfigSource(_root);

        Assert.Null(source.Read(Path.Combine(_root, "missing.conf")));
        Assert.Null(source.Trust.Refusal);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
