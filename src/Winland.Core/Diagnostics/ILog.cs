namespace Winland.Core.Diagnostics;

/// <summary>Accepts one diagnostic record. Implementations must not throw for I/O failures.</summary>
public interface ILog
{
    /// <summary>Records <paramref name="message"/> for <paramref name="area"/>.</summary>
    /// <param name="severity">How serious the record is.</param>
    /// <param name="area">Subsystem, such as Tiling.</param>
    /// <param name="message">One action or failure, in a single line.</param>
    /// <param name="error">Exception that caused the record, when there is one.</param>
    void Write(LogSeverity severity, string area, string message, Exception? error = null);
}
