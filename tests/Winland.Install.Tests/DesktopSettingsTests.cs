using System.Globalization;
using System.Text.Json;
using Winland.Install;

namespace Winland.Install.Tests;

public class DesktopSettingsTests
{
    private readonly FakeDesktop windows = new();
    private readonly MemorySettingsText file = new();
    private readonly DesktopSettings settings;

    public DesktopSettingsTests()
    {
        settings = new DesktopSettings(windows, file);
    }

    [Fact]
    public void TurnOffReadsSnapOnceThenWritesIt()
    {
        windows.Arranging = true;

        settings.TurnOff(touchpad: false);

        Assert.Equal(["get arranging", "set arranging False"], windows.Log);
        Assert.False(windows.Arranging);
        Assert.Equal(true, RecordedArranging());
        Assert.False(RecordedProperty(DesktopSettings.ThreeFingerName));
        Assert.False(RecordedProperty(DesktopSettings.FourFingerName));
    }

    [Fact]
    public void TurnOffReadsEverySwipeOnceThenWritesThem()
    {
        windows.Arranging = true;
        windows.Three = 1;
        windows.Four = 2;

        settings.TurnOff(touchpad: true);

        Assert.Equal(
            [
                "get arranging",
                "get three",
                "get four",
                "set arranging False",
                "set three 0",
                "set four 0",
            ],
            windows.Log);
        Assert.Equal(true, RecordedArranging());
        Assert.Equal(1, RecordedSlide(DesktopSettings.ThreeFingerName));
        Assert.Equal(2, RecordedSlide(DesktopSettings.FourFingerName));
    }

    [Fact]
    public void AnAbsentSwipeIsRecordedAsAbsentAndThenSetOff()
    {
        windows.Three = null;
        windows.Four = 3;

        settings.TurnOff(touchpad: true);

        Assert.Equal(JsonValueKind.Null, SlideKind(DesktopSettings.ThreeFingerName));
        Assert.Equal(3, RecordedSlide(DesktopSettings.FourFingerName));
        Assert.Equal(0, windows.Three);
        Assert.Equal(0, windows.Four);
    }

    [Fact]
    public void ASecondTurnOffKeepsTheOriginalValues()
    {
        windows.Arranging = true;
        windows.Three = 1;
        windows.Four = 2;
        settings.TurnOff(touchpad: true);
        windows.Log.Clear();

        settings.TurnOff(touchpad: true);

        Assert.Equal(true, RecordedArranging());
        Assert.Equal(1, RecordedSlide(DesktopSettings.ThreeFingerName));
        Assert.Equal(2, RecordedSlide(DesktopSettings.FourFingerName));
        Assert.Equal(
            ["get arranging", "get three", "get four", "set arranging False", "set three 0", "set four 0"],
            windows.Log);
    }

    [Fact]
    public void RestorePutsTheRecordedValuesBackAndDeletesTheRecord()
    {
        windows.Arranging = true;
        windows.Three = 1;
        windows.Four = null;
        settings.TurnOff(touchpad: true);
        windows.Log.Clear();

        settings.Restore();

        Assert.True(windows.Arranging);
        Assert.Equal(1, windows.Three);
        Assert.Null(windows.Four);
        Assert.True(file.Deleted);
        Assert.Null(file.Text);
        Assert.Equal(["set arranging True", "set three 1", "set four "], windows.Log);
    }

    [Fact]
    public void RestoreWithNoRecordChangesNothing()
    {
        settings.Restore();

        Assert.Empty(windows.Log);
        Assert.False(file.Deleted);
    }

    [Fact]
    public void ACorruptRecordIsLeftAlone()
    {
        file.Text = "{";
        windows.Arranging = true;

        var error = Assert.Throws<InvalidOperationException>(() => settings.TurnOff(touchpad: false));

        Assert.Contains(".winland", error.Message, StringComparison.Ordinal);
        Assert.Equal(["get arranging"], windows.Log);
        Assert.True(windows.Arranging);
        Assert.Equal("{", file.Text);
    }

    [Fact]
    public void RestoreLeavesACorruptRecordInPlace()
    {
        file.Text = "not json";

        var error = Assert.Throws<InvalidOperationException>(() => settings.Restore());

        Assert.Contains(".winland", error.Message, StringComparison.Ordinal);
        Assert.Empty(windows.Log);
        Assert.Equal("not json", file.Text);
        Assert.False(file.Deleted);
    }

    private bool? RecordedArranging() =>
        Root().GetProperty(DesktopSettings.WindowArrangingName).GetBoolean();

    private bool RecordedProperty(string name) =>
        Root().TryGetProperty(name, out _);

    private int RecordedSlide(string name) =>
        Root().GetProperty(name).GetInt32();

    private JsonValueKind SlideKind(string name) =>
        Root().GetProperty(name).ValueKind;

    private JsonElement Root()
    {
        using var document = JsonDocument.Parse(file.Text ?? throw new InvalidOperationException("no record"));
        return document.RootElement.Clone();
    }

    private sealed class FakeDesktop : IWindowsDesktop
    {
        public bool Arranging { get; set; } = true;

        public int? Three { get; set; }

        public int? Four { get; set; }

        public List<string> Log { get; } = [];

        public bool WindowArranging
        {
            get
            {
                Log.Add("get arranging");
                return Arranging;
            }
            set
            {
                Log.Add("set arranging " + value);
                Arranging = value;
            }
        }

        public int? ThreeFingerSlide
        {
            get
            {
                Log.Add("get three");
                return Three;
            }
            set
            {
                Log.Add("set three " + value);
                Three = value;
            }
        }

        public int? FourFingerSlide
        {
            get
            {
                Log.Add("get four");
                return Four;
            }
            set
            {
                Log.Add("set four " + (value?.ToString(CultureInfo.InvariantCulture) ?? ""));
                Four = value;
            }
        }
    }

    private sealed class MemorySettingsText : ISettingsText
    {
        public string? Text { get; set; }

        public bool Deleted { get; private set; }

        public string? Read() => Text;

        public void Write(string text)
        {
            Text = text;
            Deleted = false;
        }

        public void Delete()
        {
            Text = null;
            Deleted = true;
        }
    }
}
