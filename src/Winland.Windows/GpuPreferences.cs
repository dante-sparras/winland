using Microsoft.Win32;

namespace Winland.Windows;

/// <summary>
/// The account's graphics preferences, one string per value name. A null value is absent. False means the
/// read or the write failed and the caller should try again later.
/// </summary>
public interface IGpuPreferences
{
    bool TryRead(string name, out string? value);

    bool TryWrite(string name, string? value);
}

/// <summary>The <c>VRROptimizeEnable</c> flag inside a graphics-preference string, leaving every other flag.</summary>
public static class GpuPreferenceText
{
    public const string Flag = "VRROptimizeEnable";

    /// <summary>The value name of the account-wide flags, under the same key as each program.</summary>
    public const string GlobalName = "DirectXUserGlobalSettings";

    public static string WithVrr(string? current, bool enabled)
    {
        var flag = Flag + "=" + (enabled ? "1" : "0");
        if (string.IsNullOrEmpty(current))
            return flag + ";";

        var kept = new List<string>();
        var replaced = false;
        foreach (var part in current.Split(';'))
        {
            if (part.Length == 0)
                continue;

            var split = part.IndexOf('=');
            var key = split < 0 ? part : part[..split];
            if (key.Trim().Equals(Flag, StringComparison.OrdinalIgnoreCase))
            {
                if (!replaced)
                {
                    kept.Add(flag);
                    replaced = true;
                }

                continue;
            }

            kept.Add(part);
        }

        if (!replaced)
            kept.Add(flag);

        return string.Join(';', kept) + ";";
    }
}

/// <summary>
/// Turns variable refresh rate off for the programs a request names, and sets the account-wide flag when the
/// request says so. A value it replaces is remembered and put back by <see cref="Restore"/>. A read or a write
/// that fails is left unremembered, or still remembered, so the next request tries it again.
/// </summary>
public sealed class GpuPreferenceSession
{
    private readonly IGpuPreferences _store;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string?> _executables = new(StringComparer.OrdinalIgnoreCase);
    private bool _globalHeld;
    private string? _globalOriginal;
    private bool? _globalApplied;

    public GpuPreferenceSession(IGpuPreferences store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public VrrOriginals? Originals
    {
        get
        {
            lock (_gate)
            {
                if (!_globalHeld && _executables.Count == 0)
                    return null;

                var rows = new List<VrrExecutableOriginal>(_executables.Count);
                foreach (var (path, value) in _executables)
                    rows.Add(new VrrExecutableOriginal(path, value));

                rows.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Executable, right.Executable));
                return new VrrOriginals { Global = _globalHeld, GlobalValue = _globalOriginal, Executables = rows };
            }
        }
    }

    public void Apply(VrrRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            SyncExecutables(request.Suppressed);
            SyncGlobal(request.Global);
        }
    }

    public void Restore()
    {
        lock (_gate)
            RestoreLocked();
    }

    /// <summary>Puts saved preferences back and does not remember them as values this run changed.</summary>
    public void RestoreSaved(VrrOriginals saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        lock (_gate)
        {
            if (saved.Global)
                _store.TryWrite(GpuPreferenceText.GlobalName, saved.GlobalValue);

            foreach (var row in saved.Executables)
            {
                if (row.Executable.Length > 0)
                    _store.TryWrite(row.Executable, row.Value);
            }

            _executables.Clear();
            _globalHeld = false;
            _globalOriginal = null;
            _globalApplied = null;
        }
    }

    private void SyncExecutables(IReadOnlyList<string> desired)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in desired)
        {
            if (path.Length == 0 || !want.Add(path) || _executables.ContainsKey(path))
                continue;

            if (!_store.TryRead(path, out var original) || !_store.TryWrite(path, GpuPreferenceText.WithVrr(original, false)))
                continue;

            _executables[path] = original;
        }

        List<string>? drop = null;
        foreach (var path in _executables.Keys)
        {
            if (!want.Contains(path))
                (drop ??= []).Add(path);
        }

        if (drop is null)
            return;

        foreach (var path in drop)
        {
            if (_store.TryWrite(path, _executables[path]))
                _executables.Remove(path);
        }
    }

    private void SyncGlobal(bool? desired)
    {
        if (desired is bool enabled)
        {
            if (!_globalHeld)
            {
                if (!_store.TryRead(GpuPreferenceText.GlobalName, out var original))
                    return;

                if (!_store.TryWrite(GpuPreferenceText.GlobalName, GpuPreferenceText.WithVrr(original, enabled)))
                    return;

                _globalOriginal = original;
                _globalHeld = true;
                _globalApplied = enabled;
                return;
            }

            if (_globalApplied != enabled
                && _store.TryWrite(GpuPreferenceText.GlobalName, GpuPreferenceText.WithVrr(_globalOriginal, enabled)))
                _globalApplied = enabled;

            return;
        }

        if (_globalHeld && _store.TryWrite(GpuPreferenceText.GlobalName, _globalOriginal))
        {
            _globalHeld = false;
            _globalOriginal = null;
            _globalApplied = null;
        }
    }

    private void RestoreLocked()
    {
        foreach (var (path, original) in _executables.ToArray())
        {
            if (_store.TryWrite(path, original))
                _executables.Remove(path);
        }

        if (_globalHeld && _store.TryWrite(GpuPreferenceText.GlobalName, _globalOriginal))
        {
            _globalHeld = false;
            _globalOriginal = null;
            _globalApplied = null;
        }
    }
}

/// <summary>
/// Reads and writes HKCU\Software\Microsoft\DirectX\UserGpuPreferences. Tests do not construct this.
/// A value's name is the program's full path, or <see cref="GpuPreferenceText.GlobalName"/> for the account-wide flags.
/// </summary>
// Stryker disable once all : every member reads or writes the account's graphics preferences.
internal sealed class RegistryGpuPreferences : IGpuPreferences
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public bool TryRead(string name, out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(name))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (key is null)
                return true;

            if (key.GetValue(name) is not object found)
                return true;

            if (found is string text)
            {
                value = text;
                return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            value = null;
            return false;
        }
    }

    public bool TryWrite(string name, string? value)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        try
        {
            if (value is null)
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
                key?.DeleteValue(name, throwOnMissingValue: false);
                return true;
            }

            using var created = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            created.SetValue(name, value, RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }
}
