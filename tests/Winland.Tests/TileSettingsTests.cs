using System.Text.Json;
using FluentAssertions;
using Winland.Core;
using Winland.Core.Cli;

namespace Winoma.Tests;

[TestClass]
public sealed class TileSettingsTests
{
    [TestMethod]
    public void A_missing_key_keeps_the_default_and_a_negative_gap_is_zero()
    {
        var settings = TileSettingsFile.Read("""{"spacingPixels": -4, "browser": "  "}""");

        settings.SpacingPixels.Should().Be(0);
        settings.SpacingEnabled.Should().BeFalse();
        settings.BarVisible.Should().BeTrue();
        settings.IdleLockEnabled.Should().BeTrue();
        settings.AdministratorWindows.Should().BeFalse();
        settings.FontFace.Should().Be(TileSettings.DefaultFont);
        settings.Browser.Should().Be("  ");
    }

    [TestMethod]
    public void The_named_keys_are_the_ones_the_tiler_reads()
    {
        var settings = TileSettingsFile.Read("""
            {
              "spacingPixels": 12,
              "spacingEnabled": true,
              "squareTileEnabled": true,
              "barVisible": false,
              "idleLockEnabled": false,
              "administratorWindows": true,
              "font": "Cascadia Mono",
              "browser": "firefox"
            }
            """);

        settings.SpacingPixels.Should().Be(12);
        settings.SpacingEnabled.Should().BeTrue();
        settings.SquareTileEnabled.Should().BeTrue();
        settings.BarVisible.Should().BeFalse();
        settings.IdleLockEnabled.Should().BeFalse();
        settings.AdministratorWindows.Should().BeTrue();
        settings.FontFace.Should().Be("Cascadia Mono");
        settings.Browser.Should().Be("firefox");
    }

    [TestMethod]
    public void Writing_the_toggles_leaves_every_other_key()
    {
        var written = TileSettingsFile.WithToggles("""{"theme":"tokyo-night","spacingEnabled":false,"trayPinned":["clock"]}""", spacing: true, square: true);

        using var document = JsonDocument.Parse(written);
        var root = document.RootElement;
        root.GetProperty("theme").GetString().Should().Be("tokyo-night");
        root.GetProperty("spacingEnabled").GetBoolean().Should().BeTrue();
        root.GetProperty("squareTileEnabled").GetBoolean().Should().BeTrue();
        root.GetProperty("trayPinned").GetArrayLength().Should().Be(1);
    }

    [TestMethod]
    public void The_desktop_reply_is_the_tiler_and_an_empty_shell()
    {
        var facts = new TilerFacts([], [], null, true, false, false, false, [], 2, true, "Segoe UI Variable");
        using var document = JsonDocument.Parse(DesktopDocument.Write(facts));
        var root = document.RootElement;

        root.GetProperty("spacing").GetBoolean().Should().BeTrue();
        root.GetProperty("idleLock").GetBoolean().Should().BeFalse();
        root.GetProperty("monitor").GetInt32().Should().Be(2);
        root.GetProperty("squareTile").GetBoolean().Should().BeTrue();
        root.GetProperty("font").GetString().Should().Be("Segoe UI Variable");
        root.GetProperty("panel").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("plugins").EnumerateObject().Should().BeEmpty();
        root.GetProperty("apps").GetArrayLength().Should().Be(0);
        root.GetProperty("workspaces").GetArrayLength().Should().Be(0);
        root.GetProperty("windows").GetArrayLength().Should().Be(0);
    }
}
