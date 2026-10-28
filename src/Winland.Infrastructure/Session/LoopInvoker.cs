using System.Collections.Concurrent;
using Windows.Win32;

namespace Winland.Infrastructure;

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

    /// <summary>
    /// Queues <paramref name="action"/> on the loop thread and returns without waiting. A click on the
    /// notification-area icon arrives on that icon's thread, and waiting here would stall its menu: the loop
    /// cannot drain this queue while the click is still inside it. On the loop thread it waits for the loop's next
    /// message too, because Windows sends the loop's windows messages while it waits on another program in the middle
    /// of a step. With no loop it runs here.
    /// </summary>
    internal void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var thread = Volatile.Read(ref _thread);
        if (thread == 0)
        {
            action();
            return;
        }

        var once = new Once(action, posted: true);
        _queue.Enqueue(once);

        // The loop may have detached and drained for the last time since the thread was read. It then never reads
        // the message, so the action runs here, unless that last drain took it.
        if (!PInvoke.PostThreadMessage(thread, Message, default, default) || Volatile.Read(ref _thread) == 0)
            once.Run();
    }

    /// <summary>Runs what was handed over. The loop calls it when it receives <see cref="Message"/>.</summary>
    internal void Drain()
    {
        while (_queue.TryDequeue(out var once))
            once.Run();
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the loop thread. With no loop, on the loop thread itself, or when the
    /// loop does not take it within <paramref name="answer"/>, it runs here instead, and still only once. When
    /// the loop took it, this waits for it to finish until <paramref name="limit"/> has passed since the call.
    /// </summary>
    internal void Invoke(Action action, TimeSpan answer, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(action);
        var thread = Volatile.Read(ref _thread);
        if (thread == 0 || thread == PInvoke.GetCurrentThreadId())
        {
            action();
            return;
        }

        var started = Environment.TickCount64;
        var once = new Once(action);
        _queue.Enqueue(once);
        if (!PInvoke.PostThreadMessage(thread, Message, default, default) || !once.Wait(answer))
        {
            // The loop may have taken it just now. Returning then would let the caller, the console close
            // handler, end the process in the middle of the cleanup.
            if (!once.Run())
            {
                var left = limit - TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                if (left > TimeSpan.Zero)
                    once.Wait(left);
            }
        }

        once.Rethrow();
    }

    private sealed class Once(Action action, bool posted = false)
    {
        private readonly TaskCompletionSource _done = new();
        private int _claimed;
        private Exception? _error;

        /// <summary>Runs the action unless another thread took it first. False when one did.</summary>
        internal bool Run()
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0)
                return false;

            try
            {
                action();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (posted)
                    Console.Error.WriteLine(exception);
                else
                    _error = exception;
            }
            finally
            {
                _done.TrySetResult();
            }

            return true;
        }

        internal bool Wait(TimeSpan wait) => _done.Task.Wait(wait);

        internal void Rethrow()
        {
            if (_error is Exception error)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
    }
}
