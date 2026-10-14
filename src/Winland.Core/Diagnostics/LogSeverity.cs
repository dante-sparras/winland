namespace Winland.Core.Diagnostics;

/// <summary>How serious a log record is.</summary>
public enum LogSeverity
{
    /// <summary>Detail for a tight loop. Still written, and safe to skip when reading a failure.</summary>
    Debug,

    /// <summary>An action that happened.</summary>
    Information,

    /// <summary>The window manager continued after something failed.</summary>
    Warning,

    /// <summary>An exception stopped the action named in the record.</summary>
    Error,
}
