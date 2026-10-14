using FluentAssertions;
using Winland.Core;
using Winland.Core.Binds;
using Winland.Core.Chords;

namespace Winoma.Tests;

/// <summary>A binds file can include another, and a file with no include stays the whole map.</summary>
[TestClass]
public sealed class ChordIncludeTests
{
    private readonly List<string> _folders = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var folder in _folders)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [TestMethod]
    public void An_include_merges_chords_and_a_null_command_removes_one()
    {
        var folder = NewFolder();
        var defaults = Path.Combine(folder, "defaults.json");
        ChordBook.Defaults().Save(defaults);
        var person = Path.Combine(folder, ChordBook.FileName);
        var before = """
            {
              "include": ["defaults.json"],
              "chords": [
                { "chord": "Super+Q", "command": null },
                { "chord": "Super+Shift+C", "command": "close" }
              ]
            }
            """;
        File.WriteAllText(person, before);

        var book = ChordBook.Read(person);

        book.Resolve(new Chord(ChordModifiers.Super, ChordKey.Q)).Should().BeNull();
        book.Resolve(new Chord(ChordModifiers.Super, ChordKey.W)).Should().Be(new ShellCommand.Window(WindowAction.Close));
        book.Resolve(new Chord(ChordModifiers.Super | ChordModifiers.Shift, ChordKey.C)).Should().Be(new ShellCommand.Window(WindowAction.Close));
        book.Warnings.Should().BeEmpty();
        ChordBook.Load(person);
        File.ReadAllText(person).Should().Be(before);
    }

    [TestMethod]
    public void A_missing_include_is_a_warning_and_the_file_s_own_chords_stay()
    {
        var folder = NewFolder();
        var person = Path.Combine(folder, ChordBook.FileName);
        File.WriteAllText(person, """
            {
              "include": ["missing.json"],
              "chords": [ { "chord": "Super+Q", "command": "float" } ]
            }
            """);

        var book = ChordBook.Read(person);

        book.Resolve(new Chord(ChordModifiers.Super, ChordKey.Q)).Should().Be(new ShellCommand.Window(WindowAction.ToggleFloat));
        book.Resolve(new Chord(ChordModifiers.Super, ChordKey.W)).Should().BeNull();
        book.Warnings.Should().Contain(warning => warning.Contains("missing include", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Tiling_settings_follow_an_include_and_a_toggle_write_keeps_it()
    {
        var folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, "base.json"), """{ "spacingPixels": 20, "spacingEnabled": false }""");
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, """{ "include": ["base.json"] }""");

        var loaded = TileSettingsFile.LoadOrDefaults(path);

        loaded.SpacingPixels.Should().Be(20);
        loaded.SpacingEnabled.Should().BeFalse();
        TileSettingsFile.WriteToggles(path, spacing: true, square: false);
        File.ReadAllText(path).Should().Contain("include");
        var again = TileSettingsFile.LoadOrDefaults(path);
        again.SpacingEnabled.Should().BeTrue();
        again.SpacingPixels.Should().Be(20);
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }
}
