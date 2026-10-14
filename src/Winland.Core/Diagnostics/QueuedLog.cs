using System.Collections.Concurrent;

namespace Winland.Core.Diagnostics;

/// <summary>
/// Hands records to another log off the caller's thread.
/// The UI thread and the hook thread call the log while a chord is running, and a file append there is a stall in the desktop.
/// Debug and Information records are written from the thread pool, in order.
/// A record carries the time <see cref="Write"/> was called, and an <see cref="ITimedLog"/> writes it with that time,
/// so a burst of records is not spread over the time the appends take.
/// A Warning or Error is written before <see cref="Write"/> returns, together with every record queued before it,
/// so the lines before a crash are on disk.
/// </summary>
public sealed class QueuedLog : ILog
{
    private readonly ILog _inner;
    private readonly ITimedLog? _timed;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentQueue<Pending> _queue = new();
    private readonly object _writeGate = new();
    private int _scheduled;

    /// <summary>Queues records for <paramref name="inner"/>. Queued records are written when the process exits.</summary>
    /// <param name="inner">The log that writes. It is called by one thread at a time.</param>
    /// <param name="clock">Clock read when a record is made. Null uses the current time.</param>
    public QueuedLog(ILog inner, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _timed = inner as ITimedLog;
        _clock = clock ?? (() => DateTimeOffset.Now);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    /// <inheritdoc />
    public void Write(LogSeverity severity, string area, string message, Exception? error = null)
    {
        // The caller still hears a bad record. The pool thread must never throw.
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _queue.Enqueue(new Pending(_clock(), severity, area, message, error));
        if (severity >= LogSeverity.Warning)
        {
            Flush();
            return;
        }

        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static log => log.Drain(), this, preferLocal: false);
        }
    }

    /// <summary>Writes every queued record before returning.</summary>
    public void Flush()
    {
        lock (_writeGate)
        {
            while (_queue.TryDequeue(out var record))
            {
                try
                {
                    if (_timed is null)
                    {
                        _inner.Write(record.Severity, record.Area, record.Message, record.Error);
                    }
                    else
                    {
                        _timed.Write(record.At, record.Severity, record.Area, record.Message, record.Error);
                    }
                }
                catch (Exception)
                {
                    // A record the inner log refused must not take down the desktop or stop the records after it.
                }
            }
        }
    }

    private void Drain()
    {
        // Cleared first, so a record queued while this drain runs schedules the next one.
        Volatile.Write(ref _scheduled, 0);
        Flush();
    }

    private readonly record struct Pending(DateTimeOffset At, LogSeverity Severity, string Area, string Message, Exception? Error);
}
