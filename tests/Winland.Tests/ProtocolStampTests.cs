using FluentAssertions;
using Winland.Ipc;

namespace Winland.Tests;

/// <summary>The handshake stamp is a hash of the contract sources, so a protocol change refuses and a matching protocol connects.</summary>
[TestClass]
public sealed class ProtocolStampTests
{
    [TestMethod]
    public void The_stamp_is_a_hash_of_the_contract_source()
    {
        var texts = ProtocolStamp.Files.Select(name => File.ReadAllText(Path.Combine(ContractDirectory(), name)));

        BuildStamp.Value.Should().Be(ProtocolStamp.Hash(texts));
        BuildStamp.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [TestMethod]
    public void A_changed_contract_line_changes_the_stamp()
    {
        const string Shape = "public sealed record ShellPush(bool PickerOpen, bool Escape);";
        var changed = Shape.Replace("PickerOpen", "PickerClosed", StringComparison.Ordinal);

        ProtocolStamp.Hash([Shape]).Should().NotBe(ProtocolStamp.Hash([changed]));
        ProtocolStamp.Hash([Shape, "second"]).Should().NotBe(ProtocolStamp.Hash(["second", Shape]));
    }

    [TestMethod]
    public void Line_endings_do_not_change_the_stamp()
    {
        const string Unix = "kind hello\nstamp\n";
        var windows = Unix.Replace("\n", "\r\n", StringComparison.Ordinal);

        ProtocolStamp.Hash([Unix]).Should().Be(ProtocolStamp.Hash([windows]));
    }

    private static string ContractDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Winland.Ipc", "Handshake.cs");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "src", "Winland.Ipc");
            }

            dir = dir.Parent;
        }

        throw new AssertFailedException("Winland.Ipc sources were not found above the test output.");
    }
}
