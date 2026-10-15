namespace Winland;

public interface IConfigSource
{
    string? Read(string path);

    IReadOnlyList<string> Match(string pattern) => [];
}

public sealed record ConfigWarning(int Line, string Message, string? Source = null);

public sealed record Setting(string Keyword, string Value);

public sealed record ConfigDocument(
    IReadOnlyList<Setting> Settings,
    IReadOnlyList<BindDirective> Binds,
    IReadOnlyList<ConfigWarning> Warnings)
{
    public IReadOnlyList<WindowRule> WindowRules { get; init; } = [];

    public IReadOnlyList<WorkspaceRule> WorkspaceRules { get; init; } = WorkspaceRule.None;

    public IReadOnlyList<string> Sources { get; init; } = [];

    public IReadOnlyList<EnvironmentAssignment> Environment { get; init; } = EnvironmentAssignment.None;
}

public sealed record ConfigRead(ConfigDocument? Document, ConfigWarning? Failure = null);

public abstract record BindDirective;

public sealed record Bind(
    Modifier Modifiers,
    string Key,
    string Dispatcher,
    string Arguments,
    BindFlag Flags,
    string? Description,
    string? Submap,
    string? SubmapReset = null) : BindDirective;

public sealed record Unbind(Modifier Modifiers, string Key) : BindDirective;

public sealed record UnbindAll : BindDirective;

[Flags]
public enum Modifier
{
    None = 0,
    Super = 1 << 0,
    Alt = 1 << 1,
    Ctrl = 1 << 2,
    Shift = 1 << 3,
}

[Flags]
public enum BindFlag
{
    None = 0,
    Locked = 1 << 0,
    Release = 1 << 1,
    Repeat = 1 << 2,
    Mouse = 1 << 3,
    NonConsuming = 1 << 4,
    Transparent = 1 << 5,
    IgnoreModifiers = 1 << 6,
    MultiKey = 1 << 7,
    LongPress = 1 << 8,
    DontInhibit = 1 << 9,
    Click = 1 << 10,
    Drag = 1 << 11,
    SubmapUniversal = 1 << 12,
}
