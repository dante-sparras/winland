using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Winland.Ipc;

/// <summary>Asks Windows whether a client is still on the other end of a pipe, without reading or writing a line.</summary>
internal static class PipeProbe
{
    /// <summary>True while <paramref name="pipe"/> is still connected. A client that closed answers false.</summary>
    /// <param name="pipe">The server end.</param>
    /// <returns>False when the client has gone, or the pipe is already closed.</returns>
    internal static bool Alive(NamedPipeServerStream pipe)
    {
        try
        {
            if (!pipe.IsConnected)
            {
                return false;
            }

            if (PeekNamedPipe(pipe.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            {
                return true;
            }

            // 109 is ERROR_BROKEN_PIPE. 233 is ERROR_PIPE_NOT_CONNECTED. Anything else is left alone:
            // a transient failure must not drop a client that is still there.
            var error = Marshal.GetLastWin32Error();
            return error is not 109 and not 233;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(
        SafeHandle handle,
        IntPtr buffer,
        int bufferSize,
        IntPtr bytesRead,
        IntPtr totalBytesAvail,
        IntPtr bytesLeftThisMessage);
}
