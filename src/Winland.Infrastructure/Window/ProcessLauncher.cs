using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Winland.Infrastructure;

public static class ProcessLauncher
{
    public static int? TryLaunch(Launch launch)
    {
        try
        {
            return Launch(launch);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Could not start '{launch.CommandLine}': {exception.Message}");
            return null;
        }
    }

    public static int Launch(Launch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!IsElevated())
            return Start(launch, token: null);

        return StartLimited(launch);
    }

    internal static int LaunchHere(Launch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return Start(launch, token: null);
    }

    internal static IReadOnlyList<int> Ancestors(int processId)
    {
        if (processId <= 0)
            return [];

        try
        {
            return ReadAncestors(processId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }
    }

    // An elevated process inherits elevation into CreateProcess. The interactive
    // shell's token is the signed-in user without that elevation.
    // Stryker disable once all : needs an elevated process and a desktop shell, and unit tests have neither.
    private static unsafe int StartLimited(Launch launch)
    {
        EnablePrivilege("SeImpersonatePrivilege");
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull)
            throw new InvalidOperationException("The program was not started because the desktop shell is not available.");

        _ = PInvoke.GetWindowThreadProcessId(shell, out var pid);
        if (pid == 0)
            throw new InvalidOperationException("The program was not started because the desktop shell is not available.");

        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);
        if (process is null || process.IsInvalid)
            throw Fail();

        if (!PInvoke.OpenProcessToken(
                process,
                TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                out var token))
            throw Fail();

        using (token)
        {
            var access = TOKEN_ACCESS_MASK.TOKEN_ASSIGN_PRIMARY
                | TOKEN_ACCESS_MASK.TOKEN_QUERY
                | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE
                | TOKEN_ACCESS_MASK.TOKEN_ADJUST_DEFAULT
                | TOKEN_ACCESS_MASK.TOKEN_ADJUST_SESSIONID;
            if (!PInvoke.DuplicateTokenEx(
                    token,
                    access,
                    null,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary,
                    out var primary))
                throw Fail();

            using (primary)
                return Start(launch, primary);
        }
    }

    private static unsafe int Start(Launch launch, Microsoft.Win32.SafeHandles.SafeFileHandle? token)
    {
        var command = CommandLine(launch);
        var buffer = new char[command.Length + 1];
        command.AsSpan().CopyTo(buffer);
        var characters = buffer.AsSpan();
        var directory = Directory(launch);
        var flags = PROCESS_CREATION_FLAGS.CREATE_NEW_PROCESS_GROUP | PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW;
        void* environment = null;
        var ownsEnvironment = false;
        try
        {
            if (token is not null && PInvoke.CreateEnvironmentBlock(out environment, token, false))
            {
                ownsEnvironment = true;
                flags |= PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT;
            }

            fixed (char* desktop = "winsta0\\default")
            {
                var startup = new STARTUPINFOW
                {
                    cb = (uint)sizeof(STARTUPINFOW),
                    lpDesktop = desktop,
                };
                PROCESS_INFORMATION info;
                bool started;
                if (token is null)
                {
                    started = PInvoke.CreateProcess(
                        null,
                        ref characters,
                        null,
                        null,
                        false,
                        flags,
                        environment,
                        directory,
                        in startup,
                        out info);
                }
                else
                {
                    started = PInvoke.CreateProcessWithToken(
                        token,
                        (CREATE_PROCESS_LOGON_FLAGS)0,
                        null,
                        ref characters,
                        flags,
                        environment,
                        directory,
                        in startup,
                        out info);
                }

                var error = Marshal.GetLastPInvokeError();
                var pid = (int)info.dwProcessId;
                Close(info);
                if (!started)
                    throw new InvalidOperationException($"The program was not started ({error}).");

                return pid;
            }
        }
        finally
        {
            if (ownsEnvironment)
                PInvoke.DestroyEnvironmentBlock(environment);
        }
    }

    private static IReadOnlyList<int> ReadAncestors(int processId)
    {
        var chain = new List<int>();
        var seen = new HashSet<int> { processId };
        var current = processId;
        // 25 levels: a window opens on the Workspace the program that started it was launched on.
        for (var i = 0; i < 25; i++)
        {
            var parent = ParentOf(current);
            if (parent <= 0 || !seen.Add(parent))
                break;

            chain.Add(parent);
            current = parent;
        }

        return chain;
    }

    private static int ParentOf(int processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (process is null || process.IsInvalid)
            return 0;

        var information = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(
            process.DangerousGetHandle(),
            0,
            ref information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        if (status != 0)
            return 0;

        var parent = information.InheritedFromUniqueProcessId;
        return parent is > 0 and <= int.MaxValue ? (int)parent : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    // Stryker disable once all : adjusts the privileges of an elevated process's own token.
    private static unsafe void EnablePrivilege(string name)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        if (!PInvoke.OpenProcessToken(
                process.SafeHandle,
                TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY,
                out var token))
            throw Fail();

        using (token)
        {
            if (!PInvoke.LookupPrivilegeValue(null, name, out var luid))
                throw Fail();

            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            privileges.Privileges[0] = new LUID_AND_ATTRIBUTES
            {
                Luid = luid,
                Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,
            };

            if (!PInvoke.AdjustTokenPrivileges(new HANDLE(token.DangerousGetHandle()), false, &privileges, 0, null, null))
                throw Fail();

            if (Marshal.GetLastPInvokeError() == 1300)
                throw new InvalidOperationException("The program was not started (1300).");
        }
    }

    private static void Close(PROCESS_INFORMATION info)
    {
        if (!info.hProcess.IsNull)
            PInvoke.CloseHandle(info.hProcess);
        if (!info.hThread.IsNull)
            PInvoke.CloseHandle(info.hThread);
    }

    // /s makes cmd drop only the outer pair of quotes around the command and run the rest as written.
    // Without it, a command that starts with a quote and holds more than two loses its first and last quote.
    internal static string CommandLine(Launch launch)
    {
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return "\"" + shell + "\" /d /s /c \"" + launch.CommandLine + "\"";
    }

    private static string Directory(Launch launch)
    {
        if (!string.IsNullOrWhiteSpace(launch.WorkingDirectory))
            return launch.WorkingDirectory;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length == 0 ? Environment.CurrentDirectory : profile;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static InvalidOperationException Fail() =>
        new($"The program was not started ({Marshal.GetLastPInvokeError()}).");
}
