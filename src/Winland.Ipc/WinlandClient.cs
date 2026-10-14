using System.IO.Pipes;
using System.Text.Json;

namespace Winland.Ipc;

/// <summary>
/// The shell's Winland module. It reports Winland absent when the pipes are not up, and connects again whenever
/// Winland appears. It does not wait for Winland at startup: <see cref="Start"/> returns at once, and a call made
/// while Winland is down answers absent without blocking on a reconnect.
/// </summary>
public sealed class WinlandClient : IDisposable
{
    private readonly string _stamp;
    private readonly Action<string> _log;
    private readonly string _requests;
    private readonly string _events;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _call = new();
    private NamedPipeClientStream? _request;
    private int _connected;
    private int _disposed;

    /// <summary>Raised on a pipe thread when an event line arrives. The shell posts it to the thread that runs the command.</summary>
    public event Action<string>? Event;

    /// <summary>Raised when <see cref="Connected"/> changes.</summary>
    public event Action? ConnectionChanged;

    private WinlandClient(string stamp, Action<string> log, string? requestPipe, string? eventPipe)
    {
        _stamp = stamp;
        _log = log;
        _requests = requestPipe ?? WinlandPipes.RequestName;
        _events = eventPipe ?? WinlandPipes.EventName;
    }

    /// <summary>True while both pipes are open and the handshake succeeded.</summary>
    public bool Connected => Volatile.Read(ref _connected) != 0;

    /// <summary>
    /// Starts reconnecting in the background. Winland need not be running.
    /// </summary>
    /// <param name="stamp">This protocol. A Winland that speaks another protocol refuses the handshake.</param>
    /// <param name="log">Where a refusal is written.</param>
    /// <param name="requestPipe">The request pipe's name. Null is this session's pipe.</param>
    /// <param name="eventPipe">The event pipe's name. Null is this session's pipe.</param>
    /// <returns>The client. It is not connected yet.</returns>
    public static WinlandClient Start(string stamp, Action<string> log, string? requestPipe = null, string? eventPipe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        ArgumentNullException.ThrowIfNull(log);
        var client = new WinlandClient(stamp, log, requestPipe, eventPipe);
        Task.Run(() => client.Loop(client._stop.Token));
        return client;
    }

    /// <summary>Pushes shell state. Absent when Winland is down: the push is not saved for later.</summary>
    /// <param name="state">The state.</param>
    /// <returns>False when Winland is absent or the reply was not <c>ok</c>.</returns>
    public bool TryPush(ShellPush state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return TryCall(state.ToJson(), out var reply) && JsonLine.TryParse(reply, out var document) && Took(document, "ok");
    }

    /// <summary>Runs a query.</summary>
    /// <param name="name">The query name.</param>
    /// <param name="json">True for JSON.</param>
    /// <param name="body">The body, when the answer is true.</param>
    /// <returns>False when Winland is absent or the query was refused.</returns>
    public bool TryQuery(string name, bool json, out string body)
    {
        body = "";
        return TryCall(Wire.Query(name, json), out var reply) && Wire.TryResult(reply, out body);
    }

    /// <summary>Runs a command.</summary>
    /// <param name="command">The command word.</param>
    /// <param name="args">The words after it.</param>
    /// <param name="error">The sentence, when the command did not run. <c>Winland is not running</c> when it is absent.</param>
    /// <returns>True when Winland ran it.</returns>
    public bool TryDispatch(string command, IReadOnlyList<string> args, out string error)
    {
        error = "";
        if (!TryCall(Wire.Dispatch(command, args), out var reply))
        {
            error = "Winland is not running";
            return false;
        }

        if (Wire.TryError(reply, out error))
        {
            return false;
        }

        return true;
    }

    /// <summary>Stops reconnecting and closes the pipes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        lock (_call)
        {
            DropRequest();
        }

        SetConnected(false);
        // The token source stays. The reconnect loop may still be leaving, and disposing it makes that thread throw.
    }

    private void Loop(CancellationToken cancel)
    {
        var wait = 100;
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var events = Open(_events, cancel);
                if (events is null)
                {
                    wait = Sleep(wait, cancel);
                    continue;
                }

                NamedPipeClientStream? request;
                lock (_call)
                {
                    DropRequest();
                    request = Open(_requests, cancel);
                    _request = request;
                }

                if (request is null)
                {
                    // The event pipe came up and the request pipe did not. Fall through and try both again.
                }
                else
                {
                    SetConnected(true);
                    wait = 100;
                }

                while (request is not null && !cancel.IsCancellationRequested && LineCodec.TryRead(events, cancel, out var line))
                {
                    try
                    {
                        Event?.Invoke(line);
                    }
                    catch (Exception ex)
                    {
                        _log("an event handler failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException or OperationCanceledException)
            {
                if (cancel.IsCancellationRequested)
                {
                    return;
                }
            }
            finally
            {
                lock (_call)
                {
                    DropRequest();
                }

                SetConnected(false);
            }

            wait = Sleep(wait, cancel);
        }
    }

    private NamedPipeClientStream? Open(string name, CancellationToken cancel)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(200);
            LineCodec.Write(pipe, Handshake.Hello(_stamp));
            if (!LineCodec.TryRead(pipe, cancel, out var line) || !Handshake.TryAnswer(line, out var ok, out var stamp))
            {
                pipe.Dispose();
                return null;
            }

            if (!ok)
            {
                _log(Handshake.ClientRefusal(_stamp, stamp.Length == 0 ? "(none)" : stamp));
                pipe.Dispose();
                return null;
            }

            return pipe;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            return null;
        }
    }

    private bool TryCall(string line, out string reply)
    {
        reply = "";
        if (!Connected)
        {
            return false;
        }

        lock (_call)
        {
            if (_request is null || !_request.IsConnected)
            {
                SetConnected(false);
                return false;
            }

            try
            {
                LineCodec.Write(_request, line);
                if (!LineCodec.TryRead(_request, _stop.Token, out reply))
                {
                    DropRequest();
                    SetConnected(false);
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                DropRequest();
                SetConnected(false);
                return false;
            }
        }
    }

    private void DropRequest()
    {
        try
        {
            _request?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Already closed.
        }

        _request = null;
    }

    private void SetConnected(bool value)
    {
        var next = value ? 1 : 0;
        if (Interlocked.Exchange(ref _connected, next) == next)
        {
            return;
        }

        try
        {
            ConnectionChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log("a connection handler failed: " + ex.Message);
        }
    }

    private static int Sleep(int wait, CancellationToken cancel)
    {
        try
        {
            Task.Delay(wait, cancel).Wait(cancel);
        }
        catch (Exception ex) when (ex is OperationCanceledException or AggregateException)
        {
            // Stopping.
        }

        return Math.Min(wait * 2, 2000);
    }

    private static bool Took(JsonDocument document, string kind)
    {
        using (document)
        {
            return JsonLine.Kind(document.RootElement).Equals(kind, StringComparison.Ordinal);
        }
    }
}
