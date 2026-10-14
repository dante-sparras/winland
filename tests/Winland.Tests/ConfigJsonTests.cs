using System.Text.Json;
using FluentAssertions;
using Winland.Core;

namespace Winoma.Tests;

/// <summary>Included config files merge key by key, and binds merge per chord.</summary>
[TestClass]
public sealed class ConfigJsonTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Delete() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Includes_apply_in_order_and_the_file_itself_last()
    {
        Write("base.json", """{ "a": 1, "b": 1, "list": ["a"], "nested": { "x": 1, "y": 1 } }""");
        Write("mid.json", """{ "b": 2, "c": 2, "list": ["b"], "nested": { "y": 2, "z": 2 } }""");
        Write("top.json", """
            {
              "include": ["base.json", "mid.json"],
              "c": 3,
              "nested": { "y": null, "w": 3 }
            }
            """);

        var read = ConfigJson.Read(Path.Combine(_root, "top.json"));

        read.Notes.Should().BeEmpty();
        using var document = JsonDocument.Parse(read.Json);
        var root = document.RootElement;
        root.GetProperty("a").GetInt32().Should().Be(1);
        root.GetProperty("b").GetInt32().Should().Be(2);
        root.GetProperty("c").GetInt32().Should().Be(3);
        root.GetProperty("list").EnumerateArray().Select(item => item.GetString()).Should().Equal("b");
        root.GetProperty("nested").GetProperty("x").GetInt32().Should().Be(1);
        root.GetProperty("nested").TryGetProperty("y", out _).Should().BeFalse();
        root.GetProperty("nested").GetProperty("z").GetInt32().Should().Be(2);
        root.GetProperty("nested").GetProperty("w").GetInt32().Should().Be(3);
        root.TryGetProperty("include", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_null_removes_a_key_the_include_set()
    {
        var merged = ConfigJson.Merge("""{ "taskbar": "hidden", "gap": 8 }""", """{ "taskbar": null }""");

        using var document = JsonDocument.Parse(merged);
        document.RootElement.TryGetProperty("taskbar", out _).Should().BeFalse();
        document.RootElement.GetProperty("gap").GetInt32().Should().Be(8);
    }

    [TestMethod]
    public void Chords_merge_per_chord_and_a_null_command_removes_one()
    {
        var merged = ConfigJson.Merge(
            """
            {
              "chords": [
                { "chord": "Super+Q", "command": "close" },
                { "chord": "Super+W", "command": "close" },
                { "chord": "Super+Q", "command": "float" }
              ]
            }
            """,
            """
            {
              "chords": [
                { "chord": "Super+Q", "command": "float" },
                { "chord": "Super+E", "command": "close" },
                { "chord": "Super+W", "command": null },
                { "chord": "Super+E", "command": "quit" }
              ]
            }
            """);

        using var document = JsonDocument.Parse(merged);
        var chords = document.RootElement.GetProperty("chords").EnumerateArray().Select(entry =>
            entry.GetProperty("chord").GetString() + "=" + entry.GetProperty("command").GetString()).ToList();
        chords.Should().Equal("Super+Q=float", "Super+E=close");
    }

    [TestMethod]
    public void A_cycle_is_skipped_and_a_missing_include_is_a_note()
    {
        Write("a.json", """{ "include": ["b.json"], "fromA": 1 }""");
        Write("b.json", """{ "include": ["a.json", "gone.json"], "fromB": 2 }""");

        var read = ConfigJson.Read(Path.Combine(_root, "a.json"));

        read.Notes.Should().Contain(note => note.Contains("include cycle", StringComparison.Ordinal) && note.Contains("a.json", StringComparison.Ordinal));
        read.Notes.Should().Contain(note => note.Contains("missing include", StringComparison.Ordinal) && note.Contains("gone.json", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(read.Json);
        document.RootElement.GetProperty("fromA").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("fromB").GetInt32().Should().Be(2);
    }

    [TestMethod]
    public void A_relative_include_is_read_from_the_including_file()
    {
        Directory.CreateDirectory(Path.Combine(_root, "person"));
        Write("defaults.json", """{ "spacingPixels": 8, "spacingEnabled": false }""");
        File.WriteAllText(Path.Combine(_root, "person", "settings.json"), """{ "include": ["../defaults.json"], "spacingEnabled": true }""");

        var read = ConfigJson.Read(Path.Combine(_root, "person", "settings.json"));

        read.Notes.Should().BeEmpty();
        using var document = JsonDocument.Parse(read.Json);
        document.RootElement.GetProperty("spacingPixels").GetInt32().Should().Be(8);
        document.RootElement.GetProperty("spacingEnabled").GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void A_missing_root_is_empty_and_a_root_that_is_not_json_throws()
    {
        var missing = ConfigJson.Read(Path.Combine(_root, "absent.json"));
        missing.Json.Should().Be("{}");
        missing.Notes.Should().ContainSingle().Which.Should().Contain("missing");

        var broken = Path.Combine(_root, "broken.json");
        File.WriteAllText(broken, "{");
        var act = () => ConfigJson.Read(broken);
        act.Should().Throw<JsonException>();
    }

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(_root, name), json);
}
