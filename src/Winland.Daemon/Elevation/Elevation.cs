using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Winland.Install;

namespace Winland.Daemon;

static class Elevation
{
    // The elevated process runs in a console of its own, which is hidden. Its output comes back through two pipes
    // and is written where this process writes, so it reaches the terminal that asked and anything reading it.
    private const string OutputArgument = "--winland-output";

    // Held for the life of the process. Standard output and standard error are these pipes.
    private static NamedPipeClientStream[]? s_output;

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Relaunch(IReadOnlyList<string> args)
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is not available.");
        var launcher = Path.Combine(Path.GetDirectoryName(path)!, LogonTaskDefinition.LauncherName);
        if (File.Exists(launcher))
            path = launcher;
        var name = "winland-output-" + Guid.NewGuid().ToString("N");
        RelaunchSpec spec;
        try
        {
            spec = RelaunchPlan.Create(
                path,
                [OutputArgument, name, .. args],
                Environment.CurrentDirectory,
                RuntimeEnvironment.GetRuntimeDirectory(),
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("WINLAND_PIPE"),
                Environment.GetEnvironmentVariable("WINLAND_STATE"));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        var start = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        using var output = OpenOutput(name + "-out");
        using var error = OpenOutput(name + "-err");
        using var unanswered = new CancellationTokenSource();
        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Elevation did not start.");
            var copies = Task.WhenAll(Copy(output, Console.Out, unanswered.Token), Copy(error, Console.Error, unanswered.Token));
            process.WaitForExit();
            // A process that ended before it opened the pipes never will.
            unanswered.Cancel();
            copies.Wait();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            // The exit code says which failure this was, so the install script can name it.
            Console.Error.WriteLine("Elevation was cancelled.");
            return ex.NativeErrorCode;
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// In the elevated process, makes standard output and standard error the pipes the relaunch named, and returns
    /// the arguments without them. Any other arguments come back as they are.
    /// </summary>
    public static string[] SendOutputBack(string[] args)
    {
        if (args.Length < 2 || !args[0].Equals(OutputArgument, StringComparison.Ordinal))
            return args;

        try
        {
            var output = Connect(args[1] + "-out");
            var error = Connect(args[1] + "-err");
            s_output = [output, error];
            // Before anything reads Console, so its writers are made over the pipes. A write to a pipe the
            // relaunching process closed is dropped, as it is for a console.
            _ = SetStdHandle(StandardOutput, output.SafePipeHandle.DangerousGetHandle());
            _ = SetStdHandle(StandardError, error.SafePipeHandle.DangerousGetHandle());
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The output stays in the hidden console. The exit code still reaches the relaunching process.
        }

        return args[2..];
    }

    private static NamedPipeServerStream OpenOutput(string name) =>
        new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    // Identification lets the process that made the pipe learn who connected, but not act as them.
    // The elevated process is an administrator, so the pipe must not lend it that token.
    private static NamedPipeClientStream Connect(string name)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.Identification);
        pipe.Connect(5_000);
        return pipe;
    }

    private static async Task Copy(NamedPipeServerStream pipe, TextWriter target, CancellationToken unanswered)
    {
        try
        {
            await pipe.WaitForConnectionAsync(unanswered).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Once connected, everything is read, until the elevated process closes the pipe as it exits.
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            target.Write(buffer, 0, read);
            target.Flush();
        }
    }

    private const int StandardOutput = -11;
    private const int StandardError = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int handle, IntPtr value);
}
