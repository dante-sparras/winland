namespace Winland.Core.Diagnostics;

/// <summary>Starts work nobody waits for, without losing its failure.</summary>
public static class TaskLog
{
    /// <summary>
    /// Lets <paramref name="task"/> run on. A fault is written as a warning under <paramref name="area"/>.
    /// A cancelled task is not a failure and writes nothing.
    /// </summary>
    /// <param name="task">The work.</param>
    /// <param name="area">Log area, such as Tiling.</param>
    /// <param name="action">What the work was doing, such as "seat a new window".</param>
    public static void Forget(this Task task, string area, string action)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        task.ContinueWith(
            static (done, state) =>
            {
                var (area, action) = ((string, string))state!;
                Log.Warning(area, action + " failed", done.Exception?.GetBaseException());
            },
            (area, action),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
