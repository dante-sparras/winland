using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// Hands the newest posted value to an action on a thread of its own, so the poster does not wait for it.
/// After each run it rests for a gap, and every value posted meanwhile folds into the newest one.
/// </summary>
internal sealed class Coalescer<T> : IDisposable
    where T : class
{
    private readonly Action<T> _handle;
    private readonly TimeSpan _gap;
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _posted = new(false);
    private readonly Thread _worker;
    private T? _pending;
    private bool _disposed;

    internal Coalescer(Action<T> handle, TimeSpan gap, string name)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _handle = handle;
        _gap = gap;
        _worker = new Thread(Run) { IsBackground = true, Name = name };
        _worker.Start();
    }

    internal void Post(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_disposed)
                return;

            _pending = value;
        }

        _posted.Set();
    }

    /// <summary>Stops the thread. A value still waiting is dropped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _pending = null;
        }

        _posted.Set();
        if (_worker != Thread.CurrentThread && _worker.Join(TimeSpan.FromSeconds(2)))
            _posted.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            _posted.WaitOne();
            T? value;
            lock (_gate)
            {
                if (_disposed)
                    return;

                value = _pending;
                _pending = null;
            }

            if (value is null)
                continue;

            try
            {
                _handle(value);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }

            Thread.Sleep(_gap);
        }
    }
}
