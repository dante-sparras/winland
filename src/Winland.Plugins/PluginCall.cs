namespace Winland.Plugins;

internal static class PluginCall
{
    public static bool Run(TimeSpan budget, Action action, out Exception? error, out bool timedOut)
    {
        Exception? captured = null;
        // A pool thread may be the caller, and the pool may have no free worker.
        // Queueing the call there charges the wait for a worker against the budget,
        // so a fast Initialize is abandoned before it starts.
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        })
        {
            IsBackground = true,
            Name = "plugin-call",
        };
        thread.Start();
        if (!thread.Join(budget))
        {
            timedOut = true;
            error = null;
            return false;
        }

        timedOut = false;
        error = captured;
        return captured is null;
    }
}
