using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Winland.Windows;

public sealed class CommandServer : IDisposable
{
    private const int MaxCommandBytes = 64 * 1024;
    private const uint CloseMessage = 0x0010;

    private const int SubscriberQueueCapacity = 256;

    private readonly Core _core;
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

    public CommandServer(Core core, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _pipeName = pipeName ?? CommandProtocol.PipeName;
        _core.Cursor ??= new SystemCursor();
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

    public static RawSecurityDescriptor DescriptorFor(SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var access = SecurityFor(user).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
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
        var announced = false;
        while (!_cancel.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = Open();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!announced)
                {
                    _startError = exception;
                    _ready.Set();
                }

                return;
            }

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

            announced = true;
            _ready.Set();

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
                server.Dispose();
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

            try
            {
                Accept(server);
            }
            catch (Exception) when (_cancel.IsCancellationRequested)
            {
                server.Dispose();
                return;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                server.Dispose();
            }
        }
    }

    private void Accept(NamedPipeServerStream server)
    {
        string command;
        try
        {
            command = Read(server);
        }
        catch (InvalidOperationException exception)
        {
            Track(server);
            _ = Task.Run(() => Finish(server, () => Write(server, exception.Message)));
            return;
        }

        if (CommandProtocol.Parse(command) is CommandParsed { Request: EventsRequest })
        {
            var subscriber = new Subscriber(server);
            lock (_subscribersGate)
                _subscribers.Add(subscriber);

            Track(server);
            _ = Task.Run(() => Follow(server, subscriber));
            return;
        }

        Track(server);
        _ = Task.Run(() => Finish(server, () => Reply(server, command)));
    }

    private void Reply(NamedPipeServerStream server, string command)
    {
        var reply = _core.Execute(command);
        Apply(reply.Effect);
        Write(server, reply.Text);
    }

    private void Follow(NamedPipeServerStream server, Subscriber subscriber)
    {
        var pump = Task.Run(() => Pump(subscriber));
        try
        {
            var one = new byte[1];
            while (true)
            {
                int read;
                try
                {
                    read = server.Read(one);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    break;
                }

                if (read == 0)
                    break;
            }
        }
        finally
        {
            Drop(subscriber);
            try
            {
                if (!pump.Wait(TimeSpan.FromSeconds(2)))
                {
                    try
                    {
                        server.Dispose();
                    }
                    catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                    {
                    }

                    try
                    {
                        pump.Wait(TimeSpan.FromSeconds(1));
                    }
                    catch (AggregateException)
                    {
                    }
                }
            }
            catch (AggregateException)
            {
            }

            Release(server);
        }
    }

    private static void Pump(Subscriber subscriber)
    {
        try
        {
            var reader = subscriber.Reader;
            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var line))
                {
                    var bytes = Encoding.UTF8.GetBytes(line + "\n");
                    subscriber.Pipe.Write(bytes);
                    subscriber.Pipe.Flush();
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            subscriber.Complete();
        }
    }

    private void Finish(NamedPipeServerStream server, Action action)
    {
        try
        {
            action();
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

    private NamedPipeServerStream Open()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current user has no SID.");
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        // Setting the label afterwards is access denied (error 5) even with the
        // security privilege enabled. Creating the pipe with the label already
        // in its descriptor gives a medium client access to the elevated server.
        return elevated ? CreateLabeled(user) : Create(SecurityFor(user));
    }

    private unsafe NamedPipeServerStream CreateLabeled(SecurityIdentifier user)
    {
        var descriptor = DescriptorFor(user);
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
                0x00000003 | 0x40000000,
                0,
                255,
                0,
                0,
                0,
                ref security);
            if (handle == new IntPtr(-1))
                throw new IOException($"The command pipe did not start ({Marshal.GetLastWin32Error()}).");

            var safe = new Microsoft.Win32.SafeHandles.SafePipeHandle(handle, ownsHandle: true);
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, safe);
        }
    }

    private NamedPipeServerStream Create(PipeSecurity security) =>
        NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);

    private static string Read(NamedPipeServerStream server)
    {
        using var body = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = server.Read(one);
            if (read == 0 || one[0] == (byte)'\n')
                break;

            if (one[0] == (byte)'\r')
                continue;

            if (body.Length >= MaxCommandBytes)
                throw new InvalidOperationException("error: request too large");

            body.Write(one);
        }

        return Encoding.UTF8.GetString(body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    private static void Write(NamedPipeServerStream server, string text)
    {
        byte[] bytes;
        if (text.Contains('\n'))
        {
            var body = Encoding.UTF8.GetBytes(text);
            var header = Encoding.UTF8.GetBytes("#" + body.Length.ToString(CultureInfo.InvariantCulture) + "\n");
            bytes = new byte[header.Length + body.Length];
            header.CopyTo(bytes, 0);
            body.CopyTo(bytes, header.Length);
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(text + "\n");
        }

        server.Write(bytes);
        server.Flush();
        var one = new byte[1];
        while (server.Read(one) > 0)
            continue;
    }

    private static void Apply(Outcome outcome)
    {
        if (outcome.Close is WindowId window)
            _ = PInvoke.PostMessage(new HWND((nint)window.Value), CloseMessage, default, default);

        WindowAdapter.Apply(outcome);
    }

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
        private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(SubscriberQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        private int _closed;

        public Subscriber(NamedPipeServerStream pipe) => Pipe = pipe;

        public NamedPipeServerStream Pipe { get; }

        public ChannelReader<string> Reader => _channel.Reader;

        public void Enqueue(IReadOnlyList<string> lines)
        {
            if (Volatile.Read(ref _closed) != 0)
                return;

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
