
namespace Winland.Platform.Win32;

/// <summary>The program that has the keyboard, read from the Windows foreground window. Three Win32 calls, so the hook can ask on a key.</summary>
public sealed class FrontProgram : IFrontProgram
{
    /// <inheritdoc />
    public string? Program
    {
        get
        {
            var window = Native.GetForegroundWindow();
            if (window == 0)
            {
                return null;
            }

            _ = Native.GetWindowThreadProcessId(window, out var processId);
            var process = processId == 0 ? 0 : Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
            if (process == 0)
            {
                return null;
            }

            try
            {
                var buffer = new char[1024];
                var size = buffer.Length;
                return Native.QueryFullProcessImageName(process, 0, buffer, ref size) && size > 0 ? new string(buffer, 0, size) : null;
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }
    }
}
