using System.Diagnostics.CodeAnalysis;

namespace Winland.Core.Input;

/// <summary>
/// The lines the desktop has for the keyboard hook process and has not written to the pipe yet, oldest first.
/// Any thread adds a line and never waits. One writer thread takes them, so a hook process that has stopped reading holds that thread and no other.
/// A pointer notice only says read the pointer now, so one that still waits is dropped for a newer one. Every other line waits its turn and is never dropped.
/// One outbox serves one hook process. Closing it drops the lines that wait, so the next process is not told what was true for the last.
/// </summary>
public sealed class HookOutbox
{
    private readonly object _gate = new();
    private readonly LinkedList<HookWire> _lines = new();
    private LinkedListNode<HookWire>? _pointer;
    private bool _closed;

    /// <summary>Queues <paramref name="message"/> behind the lines that wait. Never waits.</summary>
    /// <param name="message">The line.</param>
    /// <returns>False once the outbox is closed. The line is dropped.</returns>
    public bool Add(HookWire message)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            if (message is HookWire.Pointer)
            {
                // The hook process reads the pointer when it reads the line, so the notice that waits would tell it nothing this one does not.
                if (_pointer is not null)
                {
                    _lines.Remove(_pointer);
                }

                _pointer = _lines.AddLast(message);
            }
            else
            {
                _lines.AddLast(message);
            }

            Monitor.Pulse(_gate);
            return true;
        }
    }

    /// <summary>Takes the oldest line, waiting until there is one.</summary>
    /// <param name="message">The line when this returns true.</param>
    /// <returns>False once the outbox is closed.</returns>
    public bool Take([NotNullWhen(true)] out HookWire? message)
    {
        lock (_gate)
        {
            while (true)
            {
                if (_closed)
                {
                    message = null;
                    return false;
                }

                if (_lines.First is { } first)
                {
                    _lines.RemoveFirst();
                    if (first == _pointer)
                    {
                        _pointer = null;
                    }

                    message = first.Value;
                    return true;
                }

                Monitor.Wait(_gate);
            }
        }
    }

    /// <summary>Drops the lines that wait and ends every <see cref="Take"/>. Closing twice is the same as once.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _lines.Clear();
            _pointer = null;
            Monitor.PulseAll(_gate);
        }
    }
}
