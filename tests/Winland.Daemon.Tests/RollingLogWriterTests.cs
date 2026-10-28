using System.Text;

namespace Winland.Daemon.Tests;

public class RollingLogWriterTests
{
    [Fact]
    public void TheWriterPassesEverythingOn()
    {
        var inner = new StringWriter();
        using var writer = new RollingLogWriter(inner);

        writer.WriteLine("first");
        writer.Write('x');
        writer.Write("yy");
        writer.Write(['-', 'a', 'b', '-'], 1, 2);

        Assert.Equal("first" + Environment.NewLine + "xyyab", inner.ToString());
    }

    [Fact]
    public void TheWriterUsesItsInnerEncoding()
    {
        using var writer = new RollingLogWriter(new StreamWriter(new MemoryStream(), Encoding.UTF8));

        Assert.Equal(Encoding.UTF8.WebName, writer.Encoding.WebName);
    }

    [Fact]
    public void TheLogIsCopiedToAFileWhenLogsAreEnabled()
    {
        var file = new StringWriter();
        using var writer = new RollingLogWriter(new StringWriter());

        writer.Copy(new DebugOptions { DisableLogs = false }, () => file);
        writer.Write("one ");
        writer.WriteLine("line");
        writer.Write("two\n".ToCharArray(), 0, 4);
        writer.Write("three\r");
        writer.Write('\n');

        var lines = file.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
            Assert.True(DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
        Assert.Equal(["one line", "two", "three"], lines.Select(line => line[24..].TrimEnd('\r')));

        writer.Copy(DebugOptions.Documented, () => throw new InvalidOperationException("not wanted"));
        writer.WriteLine("quiet");
        Assert.DoesNotContain("quiet", file.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningTheFileDoesNotBlockAnotherWrite()
    {
        using var writer = new RollingLogWriter(TextWriter.Null);
        using var written = new ManualResetEventSlim();
        Thread? write = null;
        var completed = false;

        writer.Copy(new DebugOptions { DisableLogs = false }, () =>
        {
            write = new Thread(() =>
            {
                writer.WriteLine("opening");
                written.Set();
            }) { IsBackground = true };
            write.Start();
            completed = written.Wait(TimeSpan.FromSeconds(2));
            return new StringWriter();
        });

        Assert.True(write!.Join(TimeSpan.FromSeconds(5)));
        Assert.True(completed);
    }

    [Fact]
    public void AFullDiskDropsTheCopyAndKeepsWritingToTheConsole()
    {
        var inner = new StringWriter();
        var file = new FullDiskWriter();
        using var writer = new RollingLogWriter(inner);
        writer.Copy(new DebugOptions { DisableLogs = false }, () => file);

        writer.WriteLine("first");
        writer.WriteLine("second");
        writer.Flush();

        Assert.True(file.Disposed);
        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine, inner.ToString());
    }

    [Fact]
    public void RestartingPreservesThePreviousLog()
    {
        var root = Directory.CreateTempSubdirectory("winland-log-");
        var path = Path.Combine(root.FullName, "winland.log");
        try
        {
            File.WriteAllText(path + ".1", "older");
            File.WriteAllText(path, "previous crash");
            using (var file = RollingLogWriter.OpenFile(path))
                file.WriteLine("new session");

            Assert.Equal("previous crash", File.ReadAllText(path + ".1"));
            Assert.Equal("new session" + Environment.NewLine, File.ReadAllText(path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFullLogRotatesInsteadOfErasingItsContents()
    {
        var root = Directory.CreateTempSubdirectory("winland-log-");
        var path = Path.Combine(root.FullName, "winland.log");
        try
        {
            using (var writer = new RollingLogWriter(TextWriter.Null))
            {
                writer.Copy(new DebugOptions { DisableLogs = false }, () => RollingLogWriter.OpenFile(path));
                writer.WriteLine(new string('x', 10 * 1024 * 1024 - 2));
                writer.WriteLine("after rotation");
            }

            Assert.EndsWith(new string('x', 100) + Environment.NewLine, File.ReadAllText(path + ".1"), StringComparison.Ordinal);
            Assert.EndsWith("after rotation" + Environment.NewLine, File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class FullDiskWriter : StringWriter
    {
        internal bool Disposed { get; private set; }

        public override void Write(string? value) => throw new IOException("disk full");

        public override void Write(ReadOnlySpan<char> buffer) => throw new IOException("disk full");

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            throw new IOException("disk full on close");
        }
    }
}
