using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;

namespace Winland.Infrastructure;

public sealed class CommandServer : IDisposable
{
    private const int SubscriberQueueCapacity = 256;

    // Room for a burst of event lines, so a subscriber's writes do not each wait for its client to read.
    private const uint OutBuffer = 64 * 1024;

    private readonly WindowManager _core;
    private readonly Action<Outcome> _apply;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _sync = new();
    private readonly object _subscribersGate = new();
    private readonly List<Subscriber> _subscribers = [];
    private readonly List<NamedPipeServerStream> _live = [];
    private readonly ManualResetEventSlim _ready = new(false);
    private NamedPipeServerStream? _listening;
    private Task? _loop;
    private Exception? _startError;
    private bool _disposed;

    /// <summary>Runs after a command the core handled, so the host can apply what the command changed.</summary>
    internal Action? AfterCommand { get; set; }

    /// <summary>How long a client has to send its request line after it connects.</summary>
    internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a client has to read its reply and close.</summary>
    internal TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many instances of the pipe may exist at once. Tests lower it to reach the limit.</summary>
    internal int MaxInstances { get; init; } = 255;

    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan LastRetry = TimeSpan.FromSeconds(2);

    public CommandServer(WindowManager core, Action<Outcome> apply, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(apply);
        _core = core;
        _apply = apply;
        _pipeName = pipeName ?? PipeServer.DefaultName;
        _core.Noticed += OnNoticed;
    }

    public static PipeSecurity SecurityFor(SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// The pipe of an elevated Winland. The user's medium-integrity programs may talk to it, but only an elevated
    /// process may add an instance, so one of them cannot serve the elevated Winland's clients in its place.
    /// </summary>
    public static RawSecurityDescriptor DescriptorFor(SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        var access = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        return new RawSecurityDescriptor(access + "S:(ML;;NW;;;S-1-16-8192)");
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop is not null)
            return;

        _loop = Task.Run(Loop);
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("The command pipe did not start.");

        if (_startError is not null)
            throw new InvalidOperationException("The command pipe did not start.", _startError);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _core.Noticed -= OnNoticed;
        _cancel.Cancel();
        NamedPipeServerStream? listening;
        lock (_sync)
        {
            listening = _listening;
            _listening = null;
        }

        listening?.Dispose();
        Subscriber[] subscribers;
        lock (_subscribersGate)
            subscribers = _subscribers.ToArray();

        foreach (var subscriber in subscribers)
            subscriber.Complete();

        NamedPipeServerStream[] live;
        lock (_live)
            live = _live.ToArray();

        foreach (var pipe in live)
        {
            try
            {
                pipe.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _ready.Dispose();
        _cancel.Dispose();
    }

    private async Task Loop()
    {
        NamedPipeServerStream? server;
        try
        {
            // The first instance claims the name. A process that already holds it makes
            // startup fail here, instead of sharing the name and some of the clients.
            server = Open(first: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _startError = exception;
            _ready.Set();
            return;
        }

        var announced = false;
        while (server is not null)
        {
            lock (_sync)
            {
                if (_cancel.IsCancellationRequested)
                {
                    server.Dispose();
                    _ready.Set();
                    return;
                }

                _listening = server;
            }

            if (!announced)
            {
                announced = true;
                _ready.Set();
            }

            try
            {
                await server.WaitForConnectionAsync(_cancel.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_cancel.IsCancellationRequested)
            {
                server.Dispose();
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var failed = server;
                server = Next();
                failed.Dispose();
                server ??= await NextAsync().ConfigureAwait(false);
                continue;
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_listening, server))
                        _listening = null;
                }
            }

            if (_cancel.IsCancellationRequested)
            {
                server.Dispose();
                return;
            }

            // The next instance is made before this one is served, so the name is never left
            // free for another process to take. The request is read off this loop, so a client
            // that connects and sends nothing does not keep the next one from connecting.
            // Tracked first, so Dispose closes it.
            var connected = server;
            server = Next();
            Track(connected);
            _ = Task.Run(() => Serve(connected));
            server ??= await NextAsync().ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream? Next()
    {
        try
        {
            return Open(first: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine(exception.Message);
            return null;
        }
    }

    // Every instance the name allows can be held for a while, by clients that connected and are slow. The
    // next instance is tried again, less and less often, until one is made or the server stops, so the pipe
    // does not go quiet for the rest of the session. Null means the server is stopping.
    private async Task<NamedPipeServerStream?> NextAsync()
    {
        var delay = FirstRetry;
        while (true)
        {
            try
            {
                await Task.Delay(delay, _cancel.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return null;
            }

            try
            {
                return Open(first: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, LastRetry.Ticks));
            }
        }
    }

    private async Task Serve(NamedPipeServerStream server)
    {
        CancellationToken stopping;
        string command;
        try
        {
            stopping = _cancel.Token;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(RequestTimeout);
            var read = await PipeFrame.ReadLineAsync(server, PipeFrame.RequestLimit, timeout.Token).ConfigureAwait(false);
            if (read is null)
            {
                Release(server);
                return;
            }

            command = read;
        }
        catch (InvalidDataException)
        {
            await Finish(server, "error: request too large", CancellationToken.None).ConfigureAwait(false);
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Timed out, gone, or the server is stopping.
            Release(server);
            return;
        }

        // A subscriber listens before its request runs, so no step that ends in between is lost to it.
        Subscriber? subscriber = null;
        if (CommandProtocol.Parse(command) is CommandParsed { Request: EventsRequest })
        {
            subscriber = new Subscriber(server);
            lock (_subscribersGate)
                _subscribers.Add(subscriber);
        }

        CommandReply reply;
        try
        {
            reply = _core.Execute(command);
            Apply(reply.Effect);
            AfterCommand?.Invoke();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception);
            if (subscriber is not null)
                Drop(subscriber);
            Release(server);
            return;
        }

        if (reply.Stream)
        {
            if (subscriber is null)
            {
                subscriber = new Subscriber(server);
                lock (_subscribersGate)
                    _subscribers.Add(subscriber);
            }

            await Follow(server, subscriber, stopping).ConfigureAwait(false);
            return;
        }

        if (subscriber is not null)
            Drop(subscriber);

        await Finish(server, reply.Text, stopping).ConfigureAwait(false);
    }

    // Every wait here is asynchronous, so an idle subscriber holds no thread. The pool also
    // runs the bind timers, and a status bar must not be able to starve them.
    private async Task Follow(NamedPipeServerStream server, Subscriber subscriber, CancellationToken stopping)
    {
        var pump = Pump(subscriber);
        try
        {
            var one = new byte[1];
            while (await server.ReadAsync(one, stopping).ConfigureAwait(false) > 0)
                continue;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        finally
        {
            Drop(subscriber);
            Release(server);
            await pump.ConfigureAwait(false);
        }
    }

    private static async Task Pump(Subscriber subscriber)
    {
        try
        {
            var reader = subscriber.Reader;
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var line))
                {
                    var bytes = Encoding.UTF8.GetBytes(line + "\n");
                    await subscriber.Pipe.WriteAsync(bytes).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            subscriber.Complete();
        }
    }

    // The client reads the reply and then closes. Waiting for that keeps the reply from being
    // cut off, and a client that never closes is let go after a while.
    private async Task Finish(NamedPipeServerStream server, string text, CancellationToken stopping)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(ReplyTimeout);
            await server.WriteAsync(PipeFrame.Frame(text), timeout.Token).ConfigureAwait(false);
            var one = new byte[1];
            while (await server.ReadAsync(one, timeout.Token).ConfigureAwait(false) > 0)
                continue;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        finally
        {
            Release(server);
        }
    }

    private void OnNoticed(Outcome outcome)
    {
        if (outcome.Events.Count == 0)
            return;

        Subscriber[] subscribers;
        lock (_subscribersGate)
        {
            if (_subscribers.Count == 0)
                return;

            subscribers = _subscribers.ToArray();
        }

        var lines = CommandProtocol.FormatStream(outcome.Events, outcome.State);
        foreach (var subscriber in subscribers)
            subscriber.Enqueue(lines);
    }

    private void Drop(Subscriber subscriber)
    {
        subscriber.Complete();
        lock (_subscribersGate)
            _subscribers.Remove(subscriber);
    }

    private void Track(NamedPipeServerStream server)
    {
        lock (_live)
            _live.Add(server);
    }

    private void Release(NamedPipeServerStream server)
    {
        lock (_live)
            _live.Remove(server);

        try
        {
            server.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    // The pipe's security is the same for every instance, so it is built once, not before each connection is served.
    private RawSecurityDescriptor? _descriptor;

    private NamedPipeServerStream Open(bool first) => Create(_descriptor ??= Descriptor(), first);

    private static RawSecurityDescriptor Descriptor()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current user has no SID.");
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        // Setting the label afterwards is access denied (error 5) even with the
        // security privilege enabled. Creating the pipe with the label already
        // in its descriptor gives a medium client access to the elevated server.
        return elevated
            ? DescriptorFor(user)
            : new RawSecurityDescriptor(SecurityFor(user).GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    }

    // Both elevations create the pipe here, so both get the same flags: remote clients are
    // refused, and the first instance fails when the name already exists.
    private unsafe NamedPipeServerStream Create(RawSecurityDescriptor descriptor, bool first)
    {
        const uint Duplex = 0x00000003;
        const uint Overlapped = 0x40000000;
        const uint FirstInstance = 0x00080000;
        const uint RejectRemoteClients = 0x00000008;
        const int AccessDenied = 5;

        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);
        fixed (byte* bytes = binary)
        {
            var security = new PipeSecurityAttributes
            {
                Length = sizeof(PipeSecurityAttributes),
                SecurityDescriptor = (IntPtr)bytes,
                InheritHandle = 0,
            };
            var handle = CreateNamedPipeW(
                @"\\.\pipe\" + _pipeName,
                Duplex | Overlapped | (first ? FirstInstance : 0),
                RejectRemoteClients,
                (uint)MaxInstances,
                OutBuffer,
                0,
                0,
                ref security);
            if (handle == new IntPtr(-1))
            {
                var error = Marshal.GetLastWin32Error();
                if (first && error == AccessDenied)
                    throw new IOException($"The command pipe did not start: the name '{_pipeName}' is already in use.");

                throw new IOException($"The command pipe did not start ({error}).");
            }

            var safe = new Microsoft.Win32.SafeHandles.SafePipeHandle(handle, ownsHandle: true);
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, safe);
        }
    }

    // The adapter asks the window to close, after its close animation when it has one.
    private void Apply(Outcome outcome) => _apply(outcome);

    [StructLayout(LayoutKind.Sequential)]
    private struct PipeSecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateNamedPipeW(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref PipeSecurityAttributes security);

    private sealed class Subscriber
    {
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

        private int _closed;

        public Subscriber(NamedPipeServerStream pipe) => Pipe = pipe;

        public NamedPipeServerStream Pipe { get; }

        public ChannelReader<string> Reader => _channel.Reader;

        // A subscriber that falls this far behind has lost its place. It is let go, so its client reconnects and asks
        // for the state again, rather than going on with events missing.
        public void Enqueue(IReadOnlyList<string> lines)
        {
            if (Volatile.Read(ref _closed) != 0)
                return;

            if (_channel.Reader.Count >= SubscriberQueueCapacity)
            {
                Complete();
                try
                {
                    Pipe.Dispose();
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                }

                return;
            }

            foreach (var line in lines)
            {
                if (!_channel.Writer.TryWrite(line))
                    return;
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            _channel.Writer.TryComplete();
        }
    }
}
