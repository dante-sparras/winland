namespace Winland.Daemon;

/// <summary>The commands that talk to the running Winland over its pipe: <c>winland dispatch</c> and the rest.</summary>
static class ControlCommand
{
    public static int Run(string line, bool events, string? pipeName)
    {
        if (events)
            return FollowEvents(pipeName);

        try
        {
            var reply = CommandClient.Transact(line, pipeName);
            if (reply.StartsWith("error:", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(reply);
                return 1;
            }

            Console.WriteLine(reply);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int FollowEvents(string? pipeName)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) =>
        {
            args.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            CommandClient.Follow("events", static line =>
            {
                Console.WriteLine(line);
                Console.Out.Flush();
            }, pipeName, cancel: cancel.Token);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
