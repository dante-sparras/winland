using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

/// <summary>
/// A temporary staging folder, plugin directory and approvals file, with fakes for git, dotnet,
/// UAC and a running Winland. The elevator runs the real elevated step in-process.
/// </summary>
internal sealed class ManagerFixture : IDisposable
{
    public const string Url = "https://example.com/plugins.git";

    public const string Manifest = "[sample]\napi_version = 2\nproject = \"Sample/Sample.csproj\"\nassembly = \"SamplePlugin.dll\"\n";

    private ManagerFixture(string root)
    {
        Root = root;
        Paths = new PluginManagerPaths(
            Path.Combine(root, "staging"),
            Path.Combine(root, "plugins"),
            Path.Combine(root, "plugins", "approved.json"),
            Path.Combine(root, "api", "Winland.PluginApi.dll"));
        Runner = new FakeRunner(this);
        Elevator = new InProcessElevator(this);
    }

    public string Root { get; }

    public PluginManagerPaths Paths { get; }

    public FakeRunner Runner { get; }

    public InProcessElevator Elevator { get; }

    public FakeRunning Running { get; } = new();

    public RecordingProtector Protector { get; } = new();

    public StringWriter Output { get; } = new();

    public StringWriter Error { get; } = new();

    public string ManifestText { get; set; } = Manifest;

    public string Commit { get; set; } = new('a', 40);

    public PluginManager Manager => new(Paths, Runner, Elevator, Running, Output, Error);

    public static ManagerFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "winland-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new ManagerFixture(root);
    }

    public static string SampleDll()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SamplePlugin.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException("The sample plugin was not built.", path);

        return path;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal sealed class FakeRunner(ManagerFixture fixture) : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public bool FailBuild { get; set; }

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
        {
            Commands.Add(fileName + " " + string.Join(' ', arguments));
            if (fileName == PluginStaging.Git && arguments[0] == "clone")
            {
                var source = arguments[^1];
                Directory.CreateDirectory(Path.Combine(source, ".git"));
                Directory.CreateDirectory(Path.Combine(source, "Sample"));
                File.WriteAllText(Path.Combine(source, "Sample", "Sample.csproj"), "<Project />");
                File.WriteAllText(Path.Combine(source, PluginManifest.FileName), fixture.ManifestText);
                return new ProcessResult(0, "");
            }

            if (fileName == PluginStaging.Git && arguments.Contains("rev-parse"))
                return new ProcessResult(0, fixture.Commit + "\n");

            // A build step that names "make <path>" makes that file, as a shell build would.
            if (fileName == PluginStaging.Shell)
            {
                var made = arguments[0].Split(" && make ") is [_, var path] ? path : null;
                if (made is not null && !FailBuild)
                {
                    var target = Path.Combine(workingDirectory, made);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(SampleDll(), target, overwrite: true);
                }

                return new ProcessResult(made is null ? 1 : 0, "step ran");
            }

            if (fileName == PluginStaging.Dotnet && arguments[0] == "publish")
            {
                if (FailBuild)
                    return new ProcessResult(1, "error CS1002: ; expected");

                var output = arguments[arguments.ToList().IndexOf("--output") + 1];
                Directory.CreateDirectory(output);
                File.Copy(SampleDll(), Path.Combine(output, "SamplePlugin.dll"), overwrite: true);
                return new ProcessResult(0, "");
            }

            return new ProcessResult(0, "");
        }
    }

    internal sealed class InProcessElevator(ManagerFixture fixture) : IElevator
    {
        public bool IsElevated { get; set; }

        public bool Cancel { get; set; }

        public int Prompts { get; private set; }

        public Action<ApplyRequest>? BeforeApply { get; set; }

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public ElevatedRun Run(IReadOnlyList<string> arguments)
        {
            Prompts++;
            Calls.Add(arguments);
            if (Cancel)
                return new ElevatedRun(false, 1, "The approval was cancelled.");

            Assert.Equal(["plugin", ApplyRequest.Verb], arguments.Take(2));
            var request = ApplyRequest.Parse(arguments.Skip(2).ToArray(), out var error);
            Assert.True(request is not null, error);
            BeforeApply?.Invoke(request);
            var result = new PluginApply(new AllowGuard(), fixture.Protector, fixture.Paths.PluginDirectory).Run(request);
            return new ElevatedRun(true, result.Ok ? 0 : 1, null) { Lines = result.Lines };
        }
    }

    internal sealed class FakeRunning : IRunningPlugins
    {
        public HashSet<string> Loaded { get; } = [];

        public List<string> Calls { get; } = [];

        public bool FailLoad { get; set; }

        public bool Unload(string name)
        {
            Calls.Add("unload " + name);
            return Loaded.Remove(name);
        }

        public bool Load(string path)
        {
            Calls.Add("load " + path);
            return !FailLoad && Loaded.Add(Path.GetFileNameWithoutExtension(path));
        }

        public bool NotRunning { get; set; }

        IReadOnlyList<string>? IRunningPlugins.Loaded() => NotRunning ? null : [.. Loaded];

        public List<string> Notifications { get; } = [];

        public void Notify(int icon, int milliseconds, string message) =>
            Notifications.Add($"{icon} {milliseconds} {message}");
    }

    internal sealed class RecordingProtector : IPluginDirectoryProtector
    {
        public List<string> Protected { get; } = [];

        public void Protect(string directory) => Protected.Add(directory);
    }

    internal sealed class AllowGuard : IPluginLocationGuard
    {
        public string? Deny(string pluginDirectory, string approvalsFile) => null;

        public string? DenyApprovals(FileStream approvals) => null;
    }

    internal sealed class DenyGuard(string message) : IPluginLocationGuard
    {
        public string? Deny(string pluginDirectory, string approvalsFile) => message;

        public string? DenyApprovals(FileStream approvals) => message;
    }
}
