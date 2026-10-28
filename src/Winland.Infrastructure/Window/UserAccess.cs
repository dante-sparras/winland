using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Winland.Infrastructure;

/// <summary>Access to user files with the desktop shell's rights, even when Winland is elevated.</summary>
public static class UserAccess
{
    private static readonly Lazy<bool> Elevated = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    public static unsafe bool HasLimitedToken()
    {
        using var identity = WindowsIdentity.GetCurrent();
        TOKEN_ELEVATION_TYPE type;
        if (!PInvoke.GetTokenInformation(identity.AccessToken, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, (uint)sizeof(TOKEN_ELEVATION_TYPE), out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return type == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
    }

    internal static unsafe bool CanSendTo(uint processId)
    {
        if (!Elevated.Value)
            return true;
        if (processId == 0)
            return false;
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            return false;
        using (token)
        {
            uint elevated;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevated, sizeof(uint), out _)
                && elevated == 0;
        }
    }

    public static T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Elevated.Value)
            return action();

        ProcessLauncher.PrepareImpersonation();
        using var token = ShellToken();
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return WindowsIdentity.RunImpersonated(identity.AccessToken, action);
    }

    public static void Run(Action action) => Run(() =>
    {
        action();
        return true;
    });

    internal static unsafe SafeFileHandle ShellToken()
    {
        var shell = PInvoke.GetShellWindow();
        _ = PInvoke.GetWindowThreadProcessId(shell, out var pid);
        if (shell.IsNull || pid == 0)
            throw new InvalidOperationException("The desktop shell is not available.");

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process,
                TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        using (token)
        {
            var access = TOKEN_ACCESS_MASK.TOKEN_ASSIGN_PRIMARY | TOKEN_ACCESS_MASK.TOKEN_QUERY
                | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_IMPERSONATE
                | TOKEN_ACCESS_MASK.TOKEN_ADJUST_DEFAULT | TOKEN_ACCESS_MASK.TOKEN_ADJUST_SESSIONID;
            if (!PInvoke.DuplicateTokenEx(token, access, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary, out var primary))
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            try
            {
                TOKEN_ELEVATION_TYPE type;
                if (!PInvoke.GetTokenInformation(primary, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, (uint)sizeof(TOKEN_ELEVATION_TYPE), out _))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull)
                    throw new InvalidOperationException("The desktop shell is elevated. Refusing to use its administrator token.");
                return primary;
            }
            catch
            {
                primary.Dispose();
                throw;
            }
        }
    }
}
