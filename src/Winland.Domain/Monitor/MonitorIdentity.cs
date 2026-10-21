using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The identity of a Monitor is its make, model and serial. Identical monitors, the same three, are
/// numbered <c> #2</c>, <c> #3</c> and so on in the order they were first seen. The first keeps the
/// identity with no number. A monitor with no model and no serial is left to its Windows name.
/// </summary>
internal sealed class MonitorIdentity
{
    // For each make, model and serial, the Windows names in the order those monitors were first seen.
    // A null slot is a monitor that has gone, and the slot stays so it keeps its number when it returns.
    private readonly Dictionary<string, List<string?>> _seen = new(StringComparer.Ordinal);

    public Monitor[] Number(IReadOnlyList<Monitor> reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var result = new Monitor[reported.Count];
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < reported.Count; i++)
        {
            var monitor = reported[i] ?? throw new ArgumentNullException(nameof(reported));
            result[i] = monitor;
            var basis = Basis(monitor);
            if (basis.Length == 0)
                continue;

            if (!groups.TryGetValue(basis, out var indices))
            {
                indices = [];
                groups[basis] = indices;
            }

            indices.Add(i);
        }

        foreach (var (basis, slots) in _seen)
        {
            if (groups.ContainsKey(basis))
                continue;

            for (var slot = 0; slot < slots.Count; slot++)
                slots[slot] = null;
        }

        foreach (var (basis, indices) in groups)
        {
            if (!_seen.TryGetValue(basis, out var slots))
            {
                slots = [];
                _seen[basis] = slots;
            }

            for (var slot = 0; slot < slots.Count; slot++)
            {
                if (slots[slot] is string name && !Listed(result, indices, name))
                    slots[slot] = null;
            }

            var taken = new HashSet<int>();
            var assigned = new int[indices.Count];
            for (var n = 0; n < indices.Count; n++)
            {
                assigned[n] = -1;
                var name = result[indices[n]].Name;
                for (var slot = 0; slot < slots.Count; slot++)
                {
                    if (taken.Contains(slot) || !Same(slots[slot], name))
                        continue;

                    assigned[n] = slot;
                    taken.Add(slot);
                    break;
                }
            }

            for (var n = 0; n < indices.Count; n++)
            {
                if (assigned[n] >= 0)
                    continue;

                var slot = -1;
                for (var candidate = 0; candidate < slots.Count; candidate++)
                {
                    if (taken.Contains(candidate) || slots[candidate] is not null)
                        continue;

                    slot = candidate;
                    break;
                }

                var windowsName = result[indices[n]].Name;
                if (slot < 0)
                {
                    slot = slots.Count;
                    slots.Add(windowsName);
                }
                else
                    slots[slot] = windowsName;

                taken.Add(slot);
                assigned[n] = slot;
            }

            for (var n = 0; n < indices.Count; n++)
                result[indices[n]] = result[indices[n]] with { Description = Format(basis, assigned[n]) };
        }

        return result;
    }

    public static string Basis(Monitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var model = monitor.Model.Trim();
        var serial = monitor.Serial.Trim();
        if (model.Length == 0 && serial.Length == 0)
            return "";

        var text = monitor.Make.Trim();
        if (model.Length > 0)
            text = text.Length == 0 ? model : text + " " + model;

        if (serial.Length > 0)
            text = text.Length == 0 ? serial : text + " " + serial;

        return text;
    }

    public static string Format(string basis, int slot) =>
        slot <= 0 ? basis : basis + " #" + (slot + 1).ToString(CultureInfo.InvariantCulture);

    private static bool Listed(Monitor[] monitors, List<int> indices, string name)
    {
        foreach (var index in indices)
        {
            if (Same(monitors[index].Name, name))
                return true;
        }

        return false;
    }

    private static bool Same(string? left, string? right) =>
        left is not null && right is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
