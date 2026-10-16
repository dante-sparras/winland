using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Winland.Windows;

/// <summary>
/// Runs the exit cleanup when the console window is closed. Log off and shut down reach the session window as
/// WM_ENDSESSION instead: a process that loads user32 does not get those console events.
/// </summary>
public sealed partial class ExitWatch : IDisposable
{
    private const uint CloseEvent = 2;
    private const uint LogoffEvent = 5;
    private const uint ShutdownEvent = 6;

    private static Action? Exit;

    private readonly Action _onExit;
    private bool _disposed;

    public ExitWatch(Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        _onExit = onExit;
        Exit = onExit;
        unsafe
        {
            _ = SetConsoleCtrlHandler(&HandleConsole, true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (ReferenceEquals(Exit, _onExit))
            Exit = null;

        unsafe
        {
            _ = SetConsoleCtrlHandler(&HandleConsole, false);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int HandleConsole(uint control)
    {
        if (control is not (CloseEvent or LogoffEvent or ShutdownEvent))
            return 0;

        var exit = Exit;
        if (exit is null)
            return 0;

        try
        {
            exit();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return 1;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetConsoleCtrlHandler(delegate* unmanaged[Stdcall]<uint, int> handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
