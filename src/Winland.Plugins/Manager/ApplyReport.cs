using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Winland.Plugins.Manager;

/// <summary>
/// Carries the elevated step's report back to the manager over a named pipe. A file in the
/// user's staging folder would let a junction there redirect a write the elevated step made.
/// The elevated side only connects, to a pipe owned by the same user, and lets the listener
/// identify it but not act as it.
/// </summary>
public static class ApplyReport
{
    public const string Prefix = "winland-apply-";

    public static bool IsName(string? name) =>
        name is not null
        && name.Length == Prefix.Length + 32
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && !name.AsSpan(Prefix.Length).ContainsAnyExcept("0123456789abcdef");

    public static bool TrySend(string name, IReadOnlyList<string> lines, TimeSpan timeout, out string? error)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!IsName(name))
        {
            error = $"'{name}' is not a report pipe.";
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                name,
                PipeDirection.Out,
                PipeOptions.CurrentUserOnly,
                TokenImpersonationLevel.Identification);
            client.Connect(timeout);
            using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            foreach (var line in lines)
                writer.WriteLine(line);

            writer.Flush();
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            error = $"Could not send the report: {exception.Message}";
            return false;
        }
    }
}

/// <summary>The manager's end of <see cref="ApplyReport"/>. Start it before the elevated step, collect after it exits.</summary>
public sealed class ApplyReportListener : IDisposable
{
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(5);

    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<string?> _reading;

    private ApplyReportListener(string name)
    {
        Name = name;
        _server = new NamedPipeServerStream(
            name,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

        // Read as soon as the elevated step connects. A write into a pipe nobody reads can
        // block until it is read, and the elevated step would then never exit.
        _reading = ReadAsync(_stop.Token);
    }

    public string Name { get; }

    public static ApplyReportListener Start() => new(ApplyReport.Prefix + Guid.NewGuid().ToString("N"));

    /// <summary>Returns the lines the elevated step sent, or none when it never connected.</summary>
    public IReadOnlyList<string> Collect()
    {
        // The elevated step has exited, so a connection it made has already completed.
        // Cancelling stops only the wait for a connection, not a read under way.
        _stop.Cancel();
        if (!_reading.Wait(ReadBudget) || _reading.Result is not string text)
            return [];

        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    private async Task<string?> ReadAsync(CancellationToken stop)
    {
        try
        {
            await _server.WaitForConnectionAsync(stop).ConfigureAwait(false);
            using var reader = new StreamReader(_server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            return await reader.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _server.Dispose();
        _stop.Dispose();
    }
}
