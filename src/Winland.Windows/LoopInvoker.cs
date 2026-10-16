using System.Collections.Concurrent;
using Windows.Win32;

namespace Winland.Windows;

/// <summary>
/// Runs an action on the thread that pumps the Daemon's messages and waits for it. The console close
/// handler runs on a thread of its own, and the cleanup it starts must not race the message loop.
/// </summary>
internal sealed class LoopInvoker
{
    internal const uint Message = 0x8000 + 0x48;

    private readonly ConcurrentQueue<Once> _queue = new();
    private uint _thread;

    /// <summary>The message loop runs on this thread from now on.</summary>
    internal void Attach(uint thread) => Volatile.Write(ref _thread, thread);

    internal void Detach() => Volatile.Write(ref _thread, 0);

    /// <summary>Runs what was handed over. The loop calls it when it receives <see cref="Message"/>.</summary>
    internal void Drain()
    {
        while (_queue.TryDequeue(out var once))
            once.Run();
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the loop thread. With no loop, on the loop thread itself, or when the
    /// loop does not answer within <paramref name="wait"/>, it runs here instead, and still only once.
    /// </summary>
    internal void Invoke(Action action, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(action);
        var thread = Volatile.Read(ref _thread);
        if (thread == 0 || thread == PInvoke.GetCurrentThreadId())
        {
            action();
            return;
        }

        var once = new Once(action);
        _queue.Enqueue(once);
        if (!PInvoke.PostThreadMessage(thread, Message, default, default) || !once.Wait(wait))
            once.Run();

        once.Rethrow();
    }

    private sealed class Once(Action action)
    {
        private readonly ManualResetEventSlim _done = new();
        private int _claimed;
        private Exception? _error;

        internal void Run()
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0)
                return;

            try
            {
                action();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _error = exception;
            }
            finally
            {
                _done.Set();
            }
        }

        internal bool Wait(TimeSpan wait) => _done.Wait(wait);

        internal void Rethrow()
        {
            if (_error is Exception error)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
    }
}
