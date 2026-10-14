using FluentAssertions;
using Winland.Core.Chords;

namespace Winland.Tests;

[TestClass]
public sealed class ChordTextTests
{
    [TestMethod]
    public void A_number_that_is_not_a_digit_key_is_not_a_key_name()
    {
        ChordText.TryParse("Super+0", out _).Should().BeFalse();
        ChordText.TryParse("Super+10", out _).Should().BeFalse();
        ChordText.TryParse("Super+999", out _).Should().BeFalse();
        ChordText.TryParse("Super+-1", out _).Should().BeFalse();

        ChordText.TryParse("Super+1", out var one).Should().BeTrue();
        one.Key.Should().Be(ChordKey.D1);
    }

    [TestMethod]
    public void Two_key_names_with_a_comma_are_not_a_key_name()
    {
        ChordText.TryParse("Super+W,Q", out _).Should().BeFalse();
        ChordText.TryParse("Super+W, Q", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Every_letter_is_a_key_name()
    {
        var keys = new HashSet<ChordKey>();
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            var text = "Super+Ctrl+" + letter;

            ChordText.TryParse(text, out var chord).Should().BeTrue(text);
            ChordText.Format(chord).Should().Be(text);
            keys.Add(chord.Key);
        }

        keys.Should().HaveCount(26);
    }

    [TestMethod]
    [DataRow("Super+Slash")]
    [DataRow("Super+Ctrl+Period")]
    [DataRow("Super+LeftBracket")]
    [DataRow("Super+RightBracket")]
    public void A_punctuation_key_is_written_as_its_word(string text)
    {
        ChordText.TryParse(text, out var chord).Should().BeTrue();

        ChordText.Format(chord).Should().Be(text);
        ChordText.TryParse(ChordText.Format(chord), out var again).Should().BeTrue();
        again.Should().Be(chord);
    }

    [TestMethod]
    public void A_symbol_is_not_a_key_name()
    {
        // The word is the one spelling of a key.
        foreach (var symbol in new[] { "/", ".", "[", "]", "=", "`" })
        {
            ChordText.TryParse("Super+" + symbol, out _).Should().BeFalse(symbol);
        }
    }

    [TestMethod]
    [DataRow("VolumeUp")]
    [DataRow("VolumeDown")]
    [DataRow("VolumeMute")]
    [DataRow("BrightnessUp")]
    [DataRow("BrightnessDown")]
    [DataRow("MediaPlayPause")]
    [DataRow("MediaNextTrack")]
    [DataRow("MediaPreviousTrack")]
    public void The_volume_brightness_and_media_keys_stay_with_windows(string name)
    {
        ChordText.TryParse(name, out _).Should().BeFalse();
        ChordText.TryParse("Super+" + name, out _).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("Super+Home")]
    [DataRow("Super+Alt+Home")]
    [DataRow("Super+Delete")]
    public void Home_and_delete_are_key_names(string text)
    {
        ChordText.TryParse(text, out var chord).Should().BeTrue();

        ChordText.Format(chord).Should().Be(text);
        ChordText.TryParse(ChordText.Format(chord), out var again).Should().BeTrue();
        again.Should().Be(chord);
    }
}
