using Windows.Win32;
using Windows.Win32.System.Power;

namespace Winland.Windows;

/// <summary>
/// Keeps the system and the display awake while a Client inhibits idle. SetThreadExecutionState
/// belongs to the thread that calls it and ends with that thread, so one thread owns it.
/// </summary>
internal sealed class IdleKeeper : IDisposable
{
    private readonly AutoResetEvent _changed = new(false);
    private readonly Thread _thread;
    private volatile bool _wanted;
    private volatile bool _stopped;
    private bool _applied;

    internal IdleKeeper()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Winland idle",
        };
        _thread.Start();
    }

    public void Set(bool inhibited)
    {
        if (_wanted == inhibited || _stopped)
            return;

        _wanted = inhibited;
        _changed.Set();
    }

    public void Dispose()
    {
        if (_stopped)
            return;

        _stopped = true;
        _wanted = false;
        _changed.Set();
        if (_thread.Join(TimeSpan.FromSeconds(2)))
            _changed.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            _changed.WaitOne();
            var wanted = _wanted && !_stopped;
            if (wanted != _applied)
            {
                var state = wanted
                    ? EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED
                    : EXECUTION_STATE.ES_CONTINUOUS;
                if (PInvoke.SetThreadExecutionState(state) == 0)
                    Console.Error.WriteLine("The idle state was not changed.");
                else
                    _applied = wanted;
            }

            if (_stopped)
                return;
        }
    }
}
