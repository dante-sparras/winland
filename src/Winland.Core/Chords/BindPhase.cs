namespace Winland.Core.Chords;

/// <summary>
/// How a chord command arrived. A plugin command carries this.
/// The shell maps it onto the phase it hands the plugin. The window manager does not run the plugin.
/// </summary>
public enum BindPhase
{
    /// <summary>A one-shot: a tap, or a command that is not held.</summary>
    Run,

    /// <summary>A held chord went down.</summary>
    Down,

    /// <summary>A held chord came up.</summary>
    Up,
}
