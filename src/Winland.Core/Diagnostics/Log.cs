namespace Winland.Core.Diagnostics;

/// <summary>Process-wide log for the window manager. Discards records until <see cref="Use"/> installs a sink.</summary>
public static class Log
{
    private static ILog _current = Discard.Instance;

    /// <summary>Installs the sink every later record uses.</summary>
    /// <param name="log">The sink. The desktop points this at the same file log as the rest of the process.</param>
    public static void Use(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        Volatile.Write(ref _current, log);
    }

    /// <summary>Records detail that is not required to see what failed.</summary>
    /// <param name="area">Subsystem.</param>
    /// <param name="message">What happened.</param>
    public static void Debug(string area, string message) =>
        Volatile.Read(ref _current).Write(LogSeverity.Debug, area, message);

    /// <summary>Records an action.</summary>
    /// <param name="area">Subsystem.</param>
    /// <param name="message">What happened.</param>
    public static void Information(string area, string message) =>
        Volatile.Read(ref _current).Write(LogSeverity.Information, area, message);

    /// <summary>Records a failure the window manager continued after.</summary>
    /// <param name="area">Subsystem.</param>
    /// <param name="message">What failed.</param>
    /// <param name="error">The exception, when there is one.</param>
    public static void Warning(string area, string message, Exception? error = null) =>
        Volatile.Read(ref _current).Write(LogSeverity.Warning, area, message, error);

    /// <summary>Records an exception that stopped an action.</summary>
    /// <param name="area">Subsystem.</param>
    /// <param name="message">Which action stopped.</param>
    /// <param name="error">The exception, when there is one.</param>
    public static void Error(string area, string message, Exception? error = null) =>
        Volatile.Read(ref _current).Write(LogSeverity.Error, area, message, error);

    private sealed class Discard : ILog
    {
        public static readonly Discard Instance = new();

        public void Write(LogSeverity severity, string area, string message, Exception? error = null)
        {
        }
    }
}
