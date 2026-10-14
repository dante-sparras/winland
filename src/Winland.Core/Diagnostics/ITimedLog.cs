namespace Winland.Core.Diagnostics;

/// <summary>
/// A log that takes the time a record happened. <see cref="QueuedLog"/> writes a record after it was made,
/// and hands the time over so the line does not carry the time of the write.
/// </summary>
public interface ITimedLog : ILog
{
    /// <summary>Records <paramref name="message"/> for <paramref name="area"/> as having happened at <paramref name="at"/>.</summary>
    /// <param name="at">When the record was made.</param>
    /// <param name="severity">How serious the record is.</param>
    /// <param name="area">Subsystem, such as Startup, Shell, or Dictation.</param>
    /// <param name="message">One action or failure, in a single line.</param>
    /// <param name="error">Exception that caused the record, when there is one.</param>
    void Write(DateTimeOffset at, LogSeverity severity, string area, string message, Exception? error = null);
}
