using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Threading;
using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// A process's executable and its ancestors, read once per process. A process id can be reused, so an entry
/// holds only while the process has the same start time. A process whose start time cannot be read is read
/// every time.
/// </summary>
internal sealed class ProcessFacts
{
    private const int Limit = 512;

    internal static readonly ProcessFacts Shared = new(StartTime, ExeOf, AncestorsOf);

    private readonly Func<uint, long> _started;
    private readonly Func<uint, string> _exe;
    private readonly Func<uint, IReadOnlyList<int>> _ancestors;
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, Facts> _known = [];

    internal ProcessFacts(Func<uint, long> started, Func<uint, string> exe, Func<uint, IReadOnlyList<int>> ancestors)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(exe);
        ArgumentNullException.ThrowIfNull(ancestors);
        _started = started;
        _exe = exe;
        _ancestors = ancestors;
    }

    internal readonly record struct Facts(long Started, string Exe, IReadOnlyList<int> Ancestors);

    internal Facts For(uint processId)
    {
        if (processId == 0)
            return new Facts(0, "", []);

        var started = _started(processId);
        lock (_gate)
        {
            if (started != 0 && _known.TryGetValue(processId, out var known) && known.Started == started)
                return known;
        }

        var facts = new Facts(started, _exe(processId), _ancestors(processId));
        if (started == 0)
            return facts;

        lock (_gate)
        {
            if (_known.Count >= Limit)
                _known.Clear();

            _known[processId] = facts;
        }

        return facts;
    }

    private static string ExeOf(uint processId) => PackagedIdentity.ExeFile(processId);

    private static IReadOnlyList<int> AncestorsOf(uint processId) =>
        processId <= int.MaxValue ? ProcessLauncher.Ancestors((int)processId) : [];

    private static long StartTime(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process is null || process.IsInvalid)
            return 0;

        return GetProcessTimes(process, out var created, out _, out _, out _) ? created : 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeHandle process, out long creation, out long exit, out long kernel, out long user);
}
