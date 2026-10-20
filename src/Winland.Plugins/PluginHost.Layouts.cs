using Winland.PluginApi;

namespace Winland.Plugins;

public sealed partial class PluginHost
{
    /// <summary>
    /// Runs a pipe command, such as the <c>layoutplace</c> a plugin layout answers with. The daemon sets it to the
    /// window host once that host exists. Until then a layout keeps the grid.
    /// </summary>
    public Func<string, string>? Command { get; set; }

    private readonly LayoutBridge _layouts;

    private readonly Lock _pendingGate = new();

    private readonly ManualResetEventSlim _idle = new(true);

    private int _pending;

    /// <summary>Waits until every layout call queued so far has answered or been abandoned.</summary>
    internal bool Settle(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        return _idle.Wait(timeout);
    }

    private static bool SameNames(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (before.Count != after.Count)
            return false;

        var seen = new HashSet<string>(before, StringComparer.Ordinal);
        foreach (var name in after)
        {
            if (!seen.Remove(name))
                return false;
        }

        return seen.Count == 0;
    }

    private void BeginPending()
    {
        lock (_pendingGate)
        {
            _pending++;
            if (_pending == 1)
                _idle.Reset();
        }
    }

    private void EndPending()
    {
        lock (_pendingGate)
        {
            if (_pending == 0)
                return;

            _pending--;
            if (_pending == 0)
                _idle.Set();
        }
    }

    private bool TryFindLayout(string name, out LoadedPlugin plugin)
    {
        lock (_gate)
        {
            foreach (var candidate in _plugins)
            {
                if (!candidate.Binding.OwnsLayout(name))
                    continue;

                plugin = candidate;
                return true;
            }
        }

        plugin = null!;
        return false;
    }

    private bool AcceptsLayoutCall(LoadedPlugin plugin)
    {
        lock (_gate)
            return !plugin.Unloading && plugin.Life != PluginLife.Disabled;
    }

    private sealed class LayoutBridge : ILayoutScripts, ILayoutSession
    {
        private readonly PluginHost _host;
        private readonly Lock _latestGate = new();
        private readonly Lock _instancesGate = new();
        private readonly Dictionary<long, LayoutRequest> _latest = [];
        private readonly Dictionary<long, Instance> _instances = [];
        private int _missingCommand;

        public LayoutBridge(PluginHost host) => _host = host;

        public void Arrange(LayoutRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            lock (_latestGate)
            {
                if (request.Targets.Count == 0)
                    _latest.Remove(request.Layout);
                else
                    _latest[request.Layout] = request with { Message = null };
            }

            if (!_host.TryFindLayout(request.Name, out var plugin))
                return;

            var job = new LayoutJob(request);
            var item = plugin.Worker.Post(() => Run(plugin, job));
            if (item is null)
                return;

            _host.BeginPending();
            var holder = new WaitHolder
            {
                Caller = Environment.CurrentManagedThreadId,
                Bridge = this,
                Plugin = plugin,
                Job = job,
                Item = item,
            };
            var handle = ThreadPool.RegisterWaitForSingleObject(
                item.Done.WaitHandle,
                static (state, timedOut) =>
                {
                    var waited = (WaitHolder)state!;
                    if (Environment.CurrentManagedThreadId != waited.Caller)
                        SpinWait.SpinUntil(() => Volatile.Read(ref waited.Handle) is not null);

                    Volatile.Read(ref waited.Handle)?.Unregister(null);
                    waited.Bridge.Complete(waited.Plugin, waited.Job, waited.Item, timedOut);
                },
                holder,
                _host._budget,
                true);
            Volatile.Write(ref holder.Handle, handle);
        }

        public void Retire(long layout)
        {
            lock (_instancesGate)
                _instances.Remove(layout);

            lock (_latestGate)
                _latest.Remove(layout);
        }

        public void Forget(string name)
        {
            long[] gone;
            lock (_instancesGate)
            {
                gone = _instances.Where(pair => pair.Value.Name == name).Select(pair => pair.Key).ToArray();
                foreach (var id in gone)
                    _instances.Remove(id);
            }

            lock (_latestGate)
            {
                foreach (var id in gone)
                    _latest.Remove(id);
            }
        }

        private void Run(LoadedPlugin plugin, LayoutJob job)
        {
            var captured = job.Request;
            if (captured.Message is null && Stale(captured))
            {
                job.Skip = true;
                return;
            }

            if (!_host.AcceptsLayoutCall(plugin))
            {
                job.Failed = true;
                job.Answer = captured.Answer(null);
                return;
            }

            Instance? instance;
            try
            {
                instance = InstanceFor(plugin, captured);
            }
            catch (Exception exception)
            {
                Fail(job, plugin, captured, exception.Message);
                return;
            }

            if (instance is null)
            {
                job.Skip = true;
                return;
            }

            if (captured.Message is not null)
            {
                try
                {
                    job.Rejection = instance.Algorithm.LayoutMessage(Describe(captured), captured.Message);
                }
                catch (Exception exception)
                {
                    Fail(job, plugin, captured, exception.Message);
                    return;
                }
            }

            var current = Latest(captured.Layout);
            if (captured.Message is null && (current is null || current.Generation != captured.Generation))
            {
                if (current is null && captured.Targets.Count == 0)
                    Diff(instance, Describe(captured));

                job.Skip = true;
                return;
            }

            if (current is null)
            {
                job.Skip = true;
                return;
            }

            try
            {
                var described = Describe(current);
                Diff(instance, described);
                var boxes = instance.Algorithm.Recalculate(described);
                if (boxes is null || !TryPlace(current, boxes, out var answer))
                {
                    Fail(job, plugin, current, boxes is null ? "returned no boxes" : "returned a box that is not finite");
                    return;
                }

                job.Answer = answer;
            }
            catch (Exception exception)
            {
                Fail(job, plugin, current, exception.Message);
            }
        }

        private void Complete(LoadedPlugin plugin, LayoutJob job, PluginWorker.WorkItem item, bool timedOut)
        {
            try
            {
                string? answer = null;
                lock (_host._gate)
                {
                    if (timedOut)
                    {
                        item.Abandon();
                        _host.NoteFailure(plugin, $"Plugin '{plugin.Name}' layout exceeded {_host.BudgetText()} and was abandoned.");
                        answer = job.Request.Answer(null);
                    }
                    else if (job.Skip)
                    {
                        answer = null;
                    }
                    else if (job.Failed)
                    {
                        if (job.Count)
                            _host.NoteFailure(plugin, job.Failure ?? $"Plugin '{plugin.Name}' layout failed.");

                        answer = job.Answer ?? job.Request.Answer(null);
                    }
                    else
                    {
                        if (job.Rejection is not null)
                            _host._log.Write($"Plugin layout '{job.Request.Name}' rejected a layout message: {job.Rejection}");

                        if (plugin.Life != PluginLife.Disabled)
                            plugin.ConsecutiveFailures = 0;

                        answer = job.Answer;
                    }
                }

                if (answer is not null)
                    Submit(answer);
            }
            finally
            {
                _host.EndPending();
            }
        }

        private void Submit(string answer)
        {
            var command = _host.Command;
            if (command is null)
            {
                if (Interlocked.Exchange(ref _missingCommand, 1) == 0)
                    _host._log.Write("Plugin layout boxes were not applied.");

                return;
            }

            try
            {
                _ = command(answer);
            }
            catch (Exception exception)
            {
                _host._log.Write($"Plugin layout boxes were not applied: {exception.Message}");
            }
        }

        private Instance? InstanceFor(LoadedPlugin plugin, LayoutRequest request)
        {
            lock (_instancesGate)
            {
                if (_instances.TryGetValue(request.Layout, out var existing))
                    return existing.Name == request.Name ? existing : null;
            }

            if (!plugin.Binding.TryFactory(request.Name, out var create))
                return null;

            var created = create() ?? throw new InvalidOperationException("The layout factory returned null.");
            var instance = new Instance(request.Name, created);
            lock (_instancesGate)
            {
                if (_instances.TryGetValue(request.Layout, out var existing))
                    return existing;

                _instances[request.Layout] = instance;
                return instance;
            }
        }

        private bool Stale(LayoutRequest request)
        {
            var latest = Latest(request.Layout);
            if (latest is null)
                return request.Targets.Count > 0;

            return latest.Generation != request.Generation;
        }

        private LayoutRequest? Latest(long layout)
        {
            lock (_latestGate)
                return _latest.TryGetValue(layout, out var request) ? request : null;
        }

        private static void Diff(Instance instance, PluginLayoutRequest request)
        {
            var seen = new HashSet<long>();
            foreach (var window in request.Windows)
            {
                seen.Add(window.Address);
                if (instance.Live.ContainsKey(window.Address))
                {
                    instance.Live[window.Address] = window;
                    continue;
                }

                instance.Algorithm.WindowAdded(window);
                instance.Live[window.Address] = window;
            }

            var gone = new List<long>();
            foreach (var address in instance.Live.Keys)
            {
                if (!seen.Contains(address))
                    gone.Add(address);
            }

            foreach (var address in gone)
            {
                instance.Algorithm.WindowRemoved(instance.Live[address]);
                instance.Live.Remove(address);
            }
        }

        private static void Fail(LayoutJob job, LoadedPlugin plugin, LayoutRequest request, string detail)
        {
            job.Failed = true;
            job.Count = true;
            job.Failure = $"Plugin '{plugin.Name}' layout failed: {detail}";
            job.Answer = request.Answer(null);
        }

        private static bool TryPlace(LayoutRequest request, IReadOnlyList<PluginLayoutBox> boxes, out string answer)
        {
            var placed = new Dictionary<int, LayoutBox>();
            var count = Math.Min(boxes.Count, request.Targets.Count);
            for (var i = 0; i < count; i++)
            {
                var box = boxes[i];
                if (!double.IsFinite(box.X) || !double.IsFinite(box.Y) || !double.IsFinite(box.Width) || !double.IsFinite(box.Height))
                {
                    answer = "";
                    return false;
                }

                placed[i] = new LayoutBox(box.X, box.Y, box.Width, box.Height);
            }

            answer = request.Answer(placed);
            return true;
        }

        private static PluginLayoutRequest Describe(LayoutRequest request)
        {
            var windows = new PluginLayoutWindow[request.Targets.Count];
            for (var i = 0; i < request.Targets.Count; i++)
            {
                var target = request.Targets[i];
                windows[i] = new PluginLayoutWindow(
                    target.Window.Value,
                    new PluginLayoutBox(target.Box.X, target.Box.Y, target.Box.Width, target.Box.Height));
            }

            return new PluginLayoutRequest(
                new PluginLayoutBox(request.Area.X, request.Area.Y, request.Area.Width, request.Area.Height),
                windows);
        }

        private sealed class LayoutJob(LayoutRequest request)
        {
            public LayoutRequest Request { get; } = request;

            public bool Skip { get; set; }

            public bool Failed { get; set; }

            public bool Count { get; set; }

            public string? Failure { get; set; }

            public string? Rejection { get; set; }

            public string? Answer { get; set; }
        }

        private sealed class WaitHolder
        {
            public int Caller { get; init; }

            public RegisteredWaitHandle? Handle;

            public required LayoutBridge Bridge { get; init; }

            public required LoadedPlugin Plugin { get; init; }

            public required LayoutJob Job { get; init; }

            public required PluginWorker.WorkItem Item { get; init; }
        }

        private sealed class Instance(string name, PluginLayout algorithm)
        {
            public string Name { get; } = name;

            public PluginLayout Algorithm { get; } = algorithm;

            public Dictionary<long, PluginLayoutWindow> Live { get; } = [];
        }
    }
}
