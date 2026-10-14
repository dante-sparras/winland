using System.Text.Json;
using FluentAssertions;
using Winland.Core.Chords;

namespace Winland.Tests;

[TestClass]
public sealed class ChordBookSaveTests
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [TestMethod]
    public void The_chord_file_matches_the_reflection_text()
    {
        var file = new ChordBook.ChordFile(1, [new("Super+Return", "terminal", false), new("Super+Space", "plugin:winoma.dictation:hold", true)]);

        JsonSerializer.Serialize(file, ChordJson.Default.ChordFile).Should().Be(JsonSerializer.Serialize(file, Indented));
    }

    [TestMethod]
    public void A_saved_book_writes_that_file_and_reads_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bindings.json");
        var book = ChordBook.Parse(
            """
            {
              "version": 1,
              "chords": [
                { "chord": "Super+Space", "command": "plugin:winoma.dictation:hold", "hold": true },
                { "chord": "Super+Return", "command": "terminal", "hold": false }
              ]
            }
            """);

        book.Save(path);

        ChordText.TryParse("Super+Return", out var open).Should().BeTrue();
        ChordText.TryParse("Super+Space", out var hold).Should().BeTrue();
        ChordCommands.TryParse("terminal", out var terminal).Should().BeTrue();
        var loaded = ChordBook.Read(path);
        loaded.Resolve(open).Should().Be(terminal);
        loaded.IsHold(hold).Should().BeTrue();
        loaded.Warnings.Should().BeEmpty();
    }
}
