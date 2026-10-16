using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Winland.Plugins.Manager;

/// <summary>
/// Carries the elevated step's report back to the manager over a named pipe. A file in the
/// user's staging folder would let a junction there redirect a write the elevated step made.
/// The elevated side only connects, to a pipe owned by itself or by the user who asked for the
/// step, and lets the listener identify it but not act as it. With an over-the-shoulder UAC
/// approval the elevated step runs as the administrator who approved it, not as that user.
/// </summary>
public static class ApplyReport
{
    public const string Prefix = "winland-apply-";

    public static bool IsName(string? name) =>
        name is not null
        && name.Length == Prefix.Length + 32
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && !name.AsSpan(Prefix.Length).ContainsAnyExcept("0123456789abcdef");

    /// <summary>
    /// Whether the elevated step may write to a pipe with this owner: its own account, or the user
    /// who asked for the step. Any other owner is a pipe someone else made under that name.
    /// </summary>
    public static bool AcceptsOwner(string? owner, string self, string? requester)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(self);
        if (string.IsNullOrWhiteSpace(owner))
            return false;

        return owner.Equals(self, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(requester) && owner.Equals(requester, StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    public static bool TrySend(string name, IReadOnlyList<string> lines, TimeSpan timeout, string? requester, out string? error)
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
                PipeOptions.None,
                TokenImpersonationLevel.Identification);
            client.Connect(timeout);
            var owner = client.GetAccessControl().GetOwner(typeof(SecurityIdentifier))?.Value;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var self = identity.User?.Value ?? throw new UnauthorizedAccessException("The current user has no SID.");
                if (!AcceptsOwner(owner, self, requester))
                    throw new UnauthorizedAccessException($"The report pipe belongs to {owner ?? "no one"}, not to the user who asked for the step.");
            }

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
[SupportedOSPlatform("windows")]
public sealed class ApplyReportListener : IDisposable
{
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(5);

    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<string?> _reading;

    private ApplyReportListener(string name)
    {
        Name = name;
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current user has no SID.");
        User = user.Value;
        _server = NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            0,
            0,
            SecurityFor(user));

        // Read as soon as the elevated step connects. A write into a pipe nobody reads can
        // block until it is read, and the elevated step would then never exit.
        _reading = ReadAsync(_stop.Token);
    }

    public string Name { get; }

    /// <summary>The SID that owns the pipe, which the elevated step is told to expect.</summary>
    public string User { get; }

    public static ApplyReportListener Start() => new(ApplyReport.Prefix + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The user, and Administrators for an elevated step that runs as another administrator.
    /// Administrators is a deny-only group in a token that is not elevated, so no other program
    /// of another user can connect.
    /// </summary>
    public static PipeSecurity SecurityFor(SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

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
