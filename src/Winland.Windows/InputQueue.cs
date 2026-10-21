using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// Carries input from the hooks to the one worker that hands it to the core. An item that replaces
/// the newest waiting one takes its place, so a burst of pointer moves while the worker is busy
/// leaves one move waiting, never a backlog ahead of the next key bind. Order is kept otherwise.
/// </summary>
internal sealed class InputQueue<T>
{
    private readonly object _gate = new();
    private readonly Func<T, T, bool> _replaces;
    private List<T> _waiting = [];
    private List<T> _taken = [];
    private bool _completed;

    /// <param name="replaces">True when the second item, posted after the first, makes the first pointless.</param>
    internal InputQueue(Func<T, T, bool> replaces)
    {
        ArgumentNullException.ThrowIfNull(replaces);
        _replaces = replaces;
    }

    /// <summary>Queues an item. False once the queue is completed.</summary>
    internal bool Post(T item)
    {
        lock (_gate)
        {
            if (_completed)
                return false;

            if (_waiting.Count > 0 && _replaces(_waiting[^1], item))
                _waiting[^1] = item;
            else
                _waiting.Add(item);

            System.Threading.Monitor.Pulse(_gate);
            return true;
        }
    }

    internal void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            System.Threading.Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Waits for input and hands over everything waiting, in order. Empty once the queue is
    /// completed and drained. For one reader: the list is reused by the next call.
    /// </summary>
    internal IReadOnlyList<T> Take()
    {
        lock (_gate)
        {
            while (_waiting.Count == 0 && !_completed)
                System.Threading.Monitor.Wait(_gate);

            (_waiting, _taken) = (_taken, _waiting);
            _waiting.Clear();
            return _taken;
        }
    }
}
