using Windows.Win32;
using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// Hides the cursor with the system magnifier, on its own thread so a pointer move never waits for it. It hides the
/// cursor over every program, not only over Winland's own windows. The cursor comes back when this process ends, even
/// when it is killed.
/// </summary>
internal static class Magnifier
{
    private static readonly Lock Gate = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static bool _cursorHidden;
    private static bool _cursorShownHidden;
    private static Thread? _worker;
    private static bool _stopping;
    private static bool _started;

    /// <summary>Hides the cursor everywhere, or shows it again.</summary>
    internal static void HideCursor(bool hidden)
    {
        lock (Gate)
        {
            if (hidden == _cursorHidden || _stopping)
                return;

            _cursorHidden = hidden;
            Start();
        }

        _ = Wake.Set();
    }

    /// <summary>Shows the cursor and ends the thread.</summary>
    internal static void Stop()
    {
        Thread? worker;
        lock (Gate)
        {
            worker = _worker;
            _stopping = true;
        }

        if (worker is null)
            return;

        _ = Wake.Set();
        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Start()
    {
        if (_worker is not null)
            return;

        _worker = new Thread(Run) { IsBackground = true, Name = "Winland magnifier" };
        _worker.Start();
    }

    private static void Run()
    {
        try
        {
            while (true)
            {
                _ = Wake.WaitOne();
                bool hidden;
                lock (Gate)
                {
                    if (_stopping)
                        break;

                    hidden = _cursorHidden;
                }

                ShowCursor(hidden);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
        finally
        {
            if (_started)
            {
                _ = PInvoke.MagShowSystemCursor(true);
                _ = PInvoke.MagUninitialize();
                _started = false;
            }
        }
    }

    // The system cursor stays hidden only while this process runs, so a Daemon that is killed leaves it visible.
    private static void ShowCursor(bool hidden)
    {
        if (hidden == _cursorShownHidden || !Started())
            return;

        if (PInvoke.MagShowSystemCursor(!hidden))
            _cursorShownHidden = hidden;
    }

    private static bool Started()
    {
        if (!_started && PInvoke.MagInitialize())
            _started = true;

        return _started;
    }
}
