using System.Collections.Concurrent;
using System.IO.Pipes;

namespace Winland.Ipc;

/// <summary>
/// Listens on the request pipe and the event pipe. A client that speaks another protocol is refused and the refusal is logged.
/// Publishing an event only queues it: the keyboard hook may call <see cref="Publish"/> and return without waiting
/// for a client to read. The event pipe is written by one thread, the one that owns that client, so a read that
/// watches for the client leaving never shares the stream. Neither this nor a client waits for the other to start.
/// </summary>
public sealed class WinlandServer : IDisposable
{
    private const int Instances = 8;
    private const int OutboxCap = 256;
    private const int RetryMilliseconds = 200;

    private readonly IWinlandEngine _engine;
    private readonly string _stamp;
    private readonly Action<string> _log;
    private readonly string _requests;
    private readonly string _events;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, EventClient> _eventClients = new();
    private readonly ConcurrentDictionary<int, Stream> _open = new();
    private readonly object _listenGate = new();
    private readonly ManualResetEventSlim _listening = new(false);
    private NamedPipeServerStream? _requestListen;
    private NamedPipeServerStream? _eventListen;
    private int _clients;
    private int _requestReady;
    private int _eventReady;
    private int _disposed;

    /// <summary>Creates a server. Call <see cref="Start"/> to listen. Nothing is opened yet.</summary>
    /// <param name="engine">Answers requests. Called on a pipe thread, never on the hook thread by this class.</param>
    /// <param name="stamp">This build. A client with another stamp is refused.</param>
    /// <param name="log">Where a refusal and a pipe failure are written. May be called on a pipe thread.</param>
    /// <param name="requestPipe">The request pipe's name. Null is <see cref="WinlandPipes.RequestName"/>.</param>
    /// <param name="eventPipe">The event pipe's name. Null is <see cref="WinlandPipes.EventName"/>.</param>
    public WinlandServer(IWinlandEngine engine, string stamp, Action<string> log, string? requestPipe = null, string? eventPipe = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        ArgumentNullException.ThrowIfNull(log);
        _engine = engine;
        _stamp = stamp;
        _log = log;
        _requests = requestPipe ?? WinlandPipes.RequestName;
        _events = eventPipe ?? WinlandPipes.EventName;
    }

    /// <summary>True once both accept loops are inside their wait. A test waits on this before it connects.</summary>
    public bool Listening => _listening.IsSet;

    /// <summary>Waits until <see cref="Listening"/>, or until <paramref name="timeout"/>.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>True when the pipes are waiting.</returns>
    public bool WaitListening(TimeSpan timeout) => _listening.Wait(timeout);

    /// <summary>Starts the accept loops. Returns at once. Winland need not have a client yet.</summary>
    public void Start()
    {
        var cancel = _stop.Token;
        Task.Run(() => Accept(_requests, request: true, cancel));
        Task.Run(() => Accept(_events, request: false, cancel));
    }

    /// <summary>
    /// Queues an event line for every client that is connected now. It does not write a pipe, so a hook callback may
    /// call it. A client that connects later does not receive it. A queue that the client is not reading is capped.
    /// </summary>
    /// <param name="line">One JSON object, with no newline.</param>
    public void Publish(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        foreach (var client in _eventClients.Values)
        {
            client.Lines.Enqueue(line);
            while (client.Lines.Count > OutboxCap && client.Lines.TryDequeue(out _))
            {
                // A client that stopped reading does not grow this queue without bound.
            }

            try
            {
                client.Posted.Set();
            }
            catch (ObjectDisposedException)
            {
                // That client already left.
            }
        }
    }

    /// <summary>Stops listening and closes every client.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        lock (_listenGate)
        {
            Close(ref _requestListen);
            Close(ref _eventListen);
        }

        foreach (var client in _eventClients.Values)
        {
            try
            {
                client.Posted.Set();
            }
            catch (ObjectDisposedException)
            {
                // The owner is already leaving.
            }
        }

        foreach (var stream in _open.Values)
        {
            Close(stream);
        }

        // The token source and the listening event stay. A pipe thread may still be leaving, and disposing them
        // makes that thread throw. One server lives as long as the process, so the handles go with it.
    }

    private void Accept(string name, bool request, CancellationToken cancel)
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    name,
                    PipeDirection.InOut,
                    Instances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lock (_listenGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        pipe.Dispose();
                        return;
                    }

                    if (request)
                    {
                        _requestListen = pipe;
                    }
                    else
                    {
                        _eventListen = pipe;
                    }
                }

                NoteReady(request);
                pipe.WaitForConnectionAsync(cancel).GetAwaiter().GetResult();
                lock (_listenGate)
                {
                    if (request && ReferenceEquals(_requestListen, pipe))
                    {
                        _requestListen = null;
                    }

                    if (!request && ReferenceEquals(_eventListen, pipe))
                    {
                        _eventListen = null;
                    }
                }

                var taken = pipe;
                pipe = null;
                var id = Interlocked.Increment(ref _clients);
                _open[id] = taken;
                if (request)
                {
                    Task.Run(() => ServeRequest(id, taken, cancel));
                }
                else
                {
                    Task.Run(() => ServeEvents(id, taken, cancel));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                _log("Winland pipe " + name + " stopped accepting: " + ex.Message);
                try
                {
                    if (cancel.WaitHandle.WaitOne(RetryMilliseconds))
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
            finally
            {
                lock (_listenGate)
                {
                    if (request && ReferenceEquals(_requestListen, pipe))
                    {
                        _requestListen = null;
                    }

                    if (!request && ReferenceEquals(_eventListen, pipe))
                    {
                        _eventListen = null;
                    }
                }

                pipe?.Dispose();
            }
        }
    }

    private void ServeRequest(int id, NamedPipeServerStream pipe, CancellationToken cancel)
    {
        try
        {
            if (!Greet(pipe, cancel))
            {
                return;
            }

            while (Volatile.Read(ref _disposed) == 0 && LineCodec.TryRead(pipe, cancel, out var line))
            {
                string reply;
                try
                {
                    reply = Answer(line);
                }
                catch (Exception ex)
                {
                    _log("a request failed: " + ex.Message);
                    reply = Wire.Error(ex.Message);
                }

                try
                {
                    LineCodec.Write(pipe, reply);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    return;
                }
            }
        }
        finally
        {
            _open.TryRemove(id, out _);
            Close(pipe);
        }
    }

    private void ServeEvents(int id, NamedPipeServerStream pipe, CancellationToken cancel)
    {
        var client = new EventClient();
        try
        {
            if (!Greet(pipe, cancel))
            {
                return;
            }

            _eventClients[id] = client;
            while (Volatile.Read(ref _disposed) == 0)
            {
                while (client.Lines.TryDequeue(out var line))
                {
                    try
                    {
                        LineCodec.Write(pipe, line);
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        return;
                    }
                }

                if (!PipeProbe.Alive(pipe))
                {
                    return;
                }

                try
                {
                    client.Posted.WaitOne(RetryMilliseconds);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
        finally
        {
            _eventClients.TryRemove(id, out _);
            _open.TryRemove(id, out _);
            client.Dispose();
            Close(pipe);
        }
    }

    private bool Greet(Stream pipe, CancellationToken cancel)
    {
        if (!LineCodec.TryRead(pipe, cancel, out var line))
        {
            return false;
        }

        if (!Handshake.Accepts(_stamp, line, out var peer))
        {
            var sentence = Handshake.ServerRefusal(peer.Length == 0 ? "(none)" : peer, _stamp);
            _log(sentence);
            try
            {
                LineCodec.Write(pipe, Handshake.Refuse(_stamp));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The client left before the refusal was written. The log still has it.
            }

            return false;
        }

        LineCodec.Write(pipe, Handshake.Accept(_stamp));
        return true;
    }

    private string Answer(string line)
    {
        if (!JsonLine.TryParse(line, out var document))
        {
            return Wire.Error("not json");
        }

        using (document)
        {
            var root = document.RootElement;
            switch (JsonLine.Kind(root))
            {
                case "state":
                    if (!ShellPush.TryParse(line, out var push))
                    {
                        return Wire.Error("state is not readable");
                    }

                    _engine.Apply(push);
                    return Wire.Ok();
                case "query":
                    return Wire.Result(_engine.Query(JsonLine.Text(root, "name"), JsonLine.Flag(root, "json")));
                case "dispatch":
                    var error = _engine.Dispatch(JsonLine.Text(root, "command"), JsonLine.Texts(root, "args"));
                    return error is null ? Wire.Ok() : Wire.Error(error);
                case "ping":
                    return Wire.Ok();
                default:
                    return Wire.Error("unknown request");
            }
        }
    }

    private void NoteReady(bool request)
    {
        if (request)
        {
            Interlocked.Exchange(ref _requestReady, 1);
        }
        else
        {
            Interlocked.Exchange(ref _eventReady, 1);
        }

        if (Volatile.Read(ref _requestReady) == 0 || Volatile.Read(ref _eventReady) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _listening.Set();
        }
        catch (ObjectDisposedException)
        {
            // Dispose won the race. The pipes are closing.
        }
    }

    private static void Close(ref NamedPipeServerStream? pipe)
    {
        var taken = pipe;
        pipe = null;
        Close(taken);
    }

    private static void Close(Stream? stream)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The client already left.
        }
    }

    private sealed class EventClient : IDisposable
    {
        internal ConcurrentQueue<string> Lines { get; } = new();

        internal AutoResetEvent Posted { get; } = new(false);

        public void Dispose() => Posted.Dispose();
    }
}
