using System.Collections.Concurrent;

namespace Winland.Plugins;

/// <summary>
/// One long-lived thread per plugin that runs every call into it, in order. The caller waits
/// up to the budget and then abandons the call, as ADR 0011 says. A call queued behind one that
/// hangs waits its own budget too, so a plugin that stays hung keeps failing and is disabled.
/// An abandoned call that has not started is skipped. A thread of its own, not the pool: the
/// caller may be a pool thread and the pool may have no free worker, which would charge the
/// wait for one against the budget.
/// </summary>
internal sealed class PluginWorker : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = [];

    public PluginWorker(string name)
    {
        var thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "plugin-" + name,
        };
        thread.Start();
    }

    public bool Run(TimeSpan budget, Action action, out Exception? error, out bool timedOut)
    {
        ArgumentNullException.ThrowIfNull(action);
        var item = new WorkItem(action);
        try
        {
            _queue.Add(item);
        }
        catch (InvalidOperationException)
        {
            timedOut = false;
            error = new ObjectDisposedException(nameof(PluginWorker));
            return false;
        }

        if (!item.Done.Wait(budget))
        {
            item.Abandon();
            timedOut = true;
            error = null;
            return false;
        }

        timedOut = false;
        error = item.Error;
        return item.Error is null;
    }

    /// <summary>
    /// Queues <paramref name="action"/> and returns without waiting. The caller times the wait, so time spent queued
    /// counts and the pool is not charged for it. Null when the worker has been disposed.
    /// </summary>
    public WorkItem? Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var item = new WorkItem(action);
        try
        {
            _queue.Add(item);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return item;
    }

    /// <summary>Ends the thread once the calls already queued are done. A call that hangs keeps it.</summary>
    public void Dispose() => _queue.CompleteAdding();

    private void Pump()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
            item.Execute();
    }

    internal sealed class WorkItem(Action action)
    {
        private int _state;

        public ManualResetEventSlim Done { get; } = new();

        public Exception? Error { get; private set; }

        public void Abandon() => Interlocked.CompareExchange(ref _state, 2, 0);

        public void Execute()
        {
            // 0 queued, 1 running, 2 abandoned before it ran.
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Error = exception;
                }
            }

            Done.Set();
        }
    }
}
