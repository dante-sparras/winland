using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class TaskbarHostTests
{
    [Fact]
    public void ReloadLeavesTheTaskbarAloneUntilTheOptionTurnsOn()
    {
        using var files = new ConfigDir("\n");
        var bars = new RecordingTaskbar();
        using var host = Host(files, bars);

        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(0, bars.Hides);
        Assert.Equal(0, bars.Restores);

        files.Write("general:hide_taskbar = true\n");
        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(1, bars.Hides);

        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(1, bars.Hides);

        files.Write("general:hide_taskbar = false\n");
        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(1, bars.Restores);

        Assert.Equal("ok", host.Command("keyword general:hide_taskbar true"));
        Assert.Equal(2, bars.Hides);
        Assert.Equal(1, bars.Restores);
    }

    [Fact]
    public void ACrashMarkerIsPutBackWhenTheOptionIsOff()
    {
        using var files = new ConfigDir("\n");
        var bars = new RecordingTaskbar { Marker = true };
        using var host = Host(files, bars);

        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(0, bars.Hides);
        Assert.Equal(1, bars.Restores);

        Assert.Equal("ok", host.Reload().Text);
        Assert.Equal(1, bars.Restores);
    }

    [Fact]
    public void ACleanExitRestoresATaskbarThisRunHidAndLeavesOneItDidNot()
    {
        using var files = new ConfigDir("general:hide_taskbar = true\n");
        var hidden = new RecordingTaskbar();
        var host = Host(files, hidden);
        Assert.Equal("ok", host.Reload().Text);
        host.Dispose();
        Assert.Equal(1, hidden.Hides);
        Assert.Equal(1, hidden.Restores);

        files.Write("\n");
        var left = new RecordingTaskbar();
        var untouched = Host(files, left);
        Assert.Equal("ok", untouched.Reload().Text);
        untouched.Dispose();
        Assert.Equal(0, left.Hides);
        Assert.Equal(0, left.Restores);
    }

    private static WindowHost Host(ConfigDir files, RecordingTaskbar bars)
    {
        var core = new Core
        {
            ReadConfig = () => new ConfigRead(ConfigParser.Parse(File.ReadAllText(files.Path))),
        };
        var host = new WindowHost(core);
        host.TaskbarSession = bars;
        return host;
    }

    private sealed class ConfigDir : IDisposable
    {
        private readonly string _root;

        public ConfigDir(string text)
        {
            _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winland-taskbar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Combine(_root, "winland.conf");
            Write(text);
        }

        public string Path { get; }

        public void Write(string text) => File.WriteAllText(Path, text);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingTaskbar : ITaskbarSession
    {
        public bool Marker { get; set; }

        public bool ChangesWorkArea => false;

        public int Hides { get; private set; }

        public int Restores { get; private set; }

        public void Hide() => Hides++;

        public void Restore() => Restores++;
    }
}
