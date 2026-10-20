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

    public IReadOnlyList<TrackpadGesture> Gestures { get; init; } = [];

    /// <summary><c>device[name]:tags</c>, in config order. A later line for the same device replaces the earlier one.</summary>
    public IReadOnlyList<DeviceTag> DeviceTags { get; init; } = [];

    /// <summary>
    /// <c>device[name]</c> sensitivity, scroll factor and rotation, in config order. A later line for the same device
    /// and field replaces the earlier one. The name is the spelling the <c>devices</c> reply uses.
    /// </summary>
    public IReadOnlyList<DevicePointer> DevicePointers { get; init; } = [];

    /// <summary>
    /// Options the pinned release has and this config set, which Winland did not apply. A later line for the same
    /// option replaces the earlier one. <c>descriptions</c> and <c>getoption</c> still report the value.
    /// </summary>
    public IReadOnlyList<Setting> Recorded { get; init; } = Array.Empty<Setting>();

    /// <summary>
    /// True only when the daemon found the config and every sourced file writable by
    /// administrators alone. Otherwise <c>[elevated]</c> launches run de-elevated (ADR 0007).
    /// </summary>
    public bool ElevationTrusted { get; init; }
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
    string? SubmapReset = null,
    IReadOnlyList<string>? Devices = null) : BindDirective;

/// <summary>Tags named for one device. A bind's device list matches a tag as it matches the device's name.</summary>
public sealed record DeviceTag(string Device, IReadOnlyList<string> Tags);

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

    /// <summary>The Caps Lock key held down. The lock itself is not a modifier, as in the pinned release.</summary>
    Caps = 1 << 4,

    /// <summary>The Num Lock key held down.</summary>
    Mod2 = 1 << 5,

    /// <summary>No key on Windows holds it, as on the pinned release's default keymap.</summary>
    Mod3 = 1 << 6,

    /// <summary>AltGr held down, on a keyboard layout that has it.</summary>
    Mod5 = 1 << 7,
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

    /// <summary>Swallow the key only when the dispatcher succeeds, as the pinned release's auto-consuming flag.</summary>
    AutoConsuming = 1 << 13,

    /// <summary>The bind matches only the devices it names. Without it, the bind matches every device but those.</summary>
    DeviceInclusive = 1 << 14,
}
