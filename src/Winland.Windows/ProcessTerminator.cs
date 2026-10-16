using System.Runtime.InteropServices;

namespace Winland.Windows;

/// <summary>
/// Ends a process. The session calls <see cref="TryTerminateOnSystem"/>; tests pass a stand-in
/// and do not call that method.
/// </summary>
public interface IProcessTerminator
{
    bool TryTerminate(long processId);
}

public static class ProcessTerminator
{
    public static bool TryTerminate(long processId, IProcessTerminator terminator)
    {
        ArgumentNullException.ThrowIfNull(terminator);
        if (processId <= 0 || processId > uint.MaxValue)
            return false;

        return terminator.TryTerminate(processId);
    }

    /// <summary>
    /// The process to end for a kill. <paramref name="live"/> is the owner read from the window now: null when
    /// the window is gone, so the recorded <paramref name="stored"/> is used, and 0 when it cannot be told.
    /// ApplicationFrameHost hosts every Store app's frame, so it is never the one ended.
    /// </summary>
    internal static long? Target(long stored, long? live, Func<long, string> exeOf)
    {
        ArgumentNullException.ThrowIfNull(exeOf);
        var candidate = live ?? stored;
        if (candidate <= 0)
            return null;

        return exeOf(candidate).Equals(PackagedIdentity.FrameExe, StringComparison.OrdinalIgnoreCase) ? null : candidate;
    }

    /// <summary>Ends <paramref name="processId"/> with the system process API. Tests do not call this.</summary>
    public static bool TryTerminateOnSystem(long processId) =>
        TryTerminate(processId, SystemProcesses.Shared);

    private sealed class SystemProcesses : IProcessTerminator
    {
        internal static readonly SystemProcesses Shared = new();

        public bool TryTerminate(long processId)
        {
            const uint Terminate = 0x0001;
            var handle = OpenProcess(Terminate, false, (uint)processId);
            if (handle == 0)
                return false;

            try
            {
                return TerminateProcess(handle, 1);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(nint handle, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(nint handle);
    }
}
