namespace Winland.Core.Binds;

/// <summary>Commands with no payload that Winland runs on the windows of the current workspace.</summary>
public enum WindowAction
{
    /// <summary>Close the focused window.</summary>
    Close,

    /// <summary>Toggle float.</summary>
    ToggleFloat,

    /// <summary>Toggle fullscreen.</summary>
    ToggleFullscreen,

    /// <summary>Toggle fullscreen inside the tile.</summary>
    ToggleTiledFullscreen,

    /// <summary>Toggle full width.</summary>
    ToggleFullWidth,

    /// <summary>Toggle dwindle and scrolling.</summary>
    ToggleLayout,

    /// <summary>Toggle the split orientation.</summary>
    ToggleSplit,

    /// <summary>Arm or disassemble a group.</summary>
    ToggleGroup,

    /// <summary>Pull the focused window out of its group.</summary>
    LeaveGroup,

    /// <summary>Toggle pseudotile.</summary>
    TogglePseudo,

    /// <summary>Toggle spacing.</summary>
    ToggleSpacing,

    /// <summary>Draw the one tiled window of the workspace as a square, or fill the tile again.</summary>
    ToggleSquareTile,

    /// <summary>Show or hide the scratchpad.</summary>
    ToggleScratchpad,

    /// <summary>Move the focused window into the scratchpad.</summary>
    SendScratchpad,

    /// <summary>Float the focused window on every workspace, or tile it again.</summary>
    ToggleSticky,

    /// <summary>Give the keyboard to the workspace in front on the next monitor.</summary>
    NextMonitor,

    /// <summary>Give the keyboard to the workspace in front on the previous monitor.</summary>
    PreviousMonitor,

    /// <summary>Remember the focused window's width.</summary>
    SaveWidth,

    /// <summary>Resize the focused window back to its remembered width.</summary>
    RestoreWidth,

    /// <summary>Hide the bar and turn spacing off together, or restore both.</summary>
    ToggleFullscreenDesktop,
}
