using System.Runtime.ExceptionServices;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// The thread the low-level keyboard and mouse hooks run on. Windows calls a low-level hook on the
/// thread that installed it, and holds up all input until it returns; a callback that is too slow
/// is removed without notice. This thread only pumps messages: the callbacks decide from the
/// published bind table whether to swallow and queue the rest for the bind worker, and the hooks'
/// timers post here. Window events stay on the main thread, so a hung window, a slow
/// <c>SetWindowPos</c> or a reload holding the core lock never delays the keyboard or the mouse.
/// </summary>
internal sealed class InputThread : IDisposable
{
    private const uint QuitMessage = 0x0012;
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    private readonly Action _start;
    private readonly Func<uint, bool> _service;
    private readonly Action _stop;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private ExceptionDispatchInfo? _failure;
    private uint _id;
    private bool _disposed;

    /// <param name="start">Installs the hooks. Runs on the input thread.</param>
    /// <param name="service">Handles a thread message posted to the input thread. True when handled.</param>
    /// <param name="stop">Removes the hooks. Runs on the input thread after its last message.</param>
    internal InputThread(Action start, Func<uint, bool> service, Action stop)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(stop);
        _start = start;
        _service = service;
        _stop = stop;
    }

    internal uint Id => _id;

    /// <summary>Starts the thread and waits until <c>start</c> has run there. Throws what it threw.</summary>
    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
            return;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Winland input",
            // Nothing slow runs here, and input waits on it.
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        _started.Wait();
        if (_failure is not null)
        {
            _thread.Join();
            _thread = null;
            _failure.Throw();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_thread is not null)
        {
            _ = PInvoke.PostThreadMessage(_id, QuitMessage, default, default);
            if (!_thread.Join(StopWait))
                Console.Error.WriteLine("The input thread did not stop.");
        }

        _started.Dispose();
    }

    private void Run()
    {
        _id = PInvoke.GetCurrentThreadId();

        // Gives the thread its message queue before anything can post to it.
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        try
        {
            _start();
        }
        catch (Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
            Stop();
            _started.Set();
            return;
        }

        _started.Set();
        try
        {
            while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
            {
                if (message.hwnd.IsNull && Service(message.message))
                    continue;

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
        }
        finally
        {
            Stop();
        }
    }

    private bool Service(uint message)
    {
        try
        {
            return _service(message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            return true;
        }
    }

    private void Stop()
    {
        try
        {
            _stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }
}
