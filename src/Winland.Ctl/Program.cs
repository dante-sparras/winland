
if (!ControlInvocation.TryCreate(args, out var line, out var events))
    return Usage();

if (events)
    return FollowEvents();

try
{
    var reply = CommandClient.Transact(line, PipeName());
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

static int FollowEvents()
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
        }, PipeName(), cancel: cancel.Token);
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

static string? PipeName()
{
    var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
    return string.IsNullOrWhiteSpace(configured) ? null : configured;
}

static int Usage()
{
    Console.Error.Write(ControlInvocation.Usage);
    return 2;
}
