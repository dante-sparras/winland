using Winland.Core;
using Winland.Core.Diagnostics;
using Winland.Platform.Win32;
using Winland.Ipc;
using Winland.Platform;

namespace Winland;

/// <summary>The window manager process. The shell is another process and talks to this one over the pipes.</summary>
public static class Program
{
    private static readonly Mutex Single = new(false, @"Local\Winland");

    [STAThread]
    private static void Main(string[] args)
    {
        var log = new QueuedLog(new FileLog(Path.Combine(ConfigFolder.Path, "winland.log")));
        Log.Use(log);
        Log.Information("Winland", args.Length == 0 ? "process started" : "process started " + string.Join(' ', args));

        if (WinlandWatch.IsWatch(args))
        {
            WinlandWatch.Watch(args, log.Flush);
            return;
        }

        if (args.Contains("--place"))
        {
            WinlandAnswers.Arm();
            ElevatedBroker.Run();
            log.Flush();
            return;
        }

        if (args.Contains("--give-back"))
        {
            GiveBack(log);
            return;
        }

        if (!Take())
        {
            Log.Information("Winland", "another Winland holds Local\\Winland; exiting");
            log.Flush();
            return;
        }

        WinlandFiles.Ensure(AppContext.BaseDirectory);
        WinlandAnswers.Arm();
        WinlandWatch.Arm();
        var found = InheritWord(args);
        HiddenStore.Inherit(found);
        if (!Lifetime.TryWrite(LifetimePath(), LifetimeWord.Running, out var error))
        {
            Log.Warning("Winland", "could not write lifetime", error);
        }

        new WmHost().Run();
        log.Flush();
    }

    /// <summary>True when a window manager holds <c>Local\Winland</c>.</summary>
    internal static bool Running()
    {
        try
        {
            if (!Mutex.TryOpenExisting(@"Local\Winland", out var held))
            {
                return false;
            }

            held.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool Take()
    {
        try
        {
            return Single.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            Log.Warning("Winland", "the previous Winland abandoned Local\\Winland");
            return true;
        }
    }

    private static LifetimeWord InheritWord(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--inherit")
            {
                return Lifetime.Read(args[i + 1]);
            }
        }

        if (!Lifetime.TryRead(LifetimePath(), out var word, out var error))
        {
            Log.Warning("Winland", "could not read lifetime, so the desktop that wrote it is taken to have crashed", error);
        }

        return word;
    }

    private static string LifetimePath() => Path.Combine(ConfigFolder.Path, Lifetime.FileName);

    /// <summary>
    /// Gives Windows its desktop back when no window manager is running. A running one is asked to stop, and this waits until it has.
    /// </summary>
    private static void GiveBack(QueuedLog log)
    {
        if (Running())
        {
            AskStop();
            var until = Environment.TickCount64 + 5000;
            while (Running() && Environment.TickCount64 < until)
            {
                Thread.Sleep(50);
            }
        }

        if (Running())
        {
            Log.Warning("Winland", "give-back left a window manager running");
            log.Flush();
            return;
        }

        HiddenStore.Rescue();
        WindowFade.RestoreLeft();
        WindowGateway.RestoreCaptionsWithoutDesktop();

        Log.Information("Winland", "gave Windows back what a stopped window manager held");
        log.Flush();
    }

    private static void AskStop()
    {
        var client = WinlandClient.Start(BuildStamp.Value, message => Log.Warning("Winland", message));
        var until = Environment.TickCount64 + 2000;
        while (!client.Connected && Environment.TickCount64 < until)
        {
            Thread.Sleep(20);
        }

        if (!client.TryDispatch("stop", [], out var error))
        {
            Log.Warning("Winland", "give-back could not ask the window manager to stop: " + error);
        }

        client.Dispose();
    }
}
