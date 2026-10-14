namespace Winland.Core.Binds;

/// <summary>
/// An <see cref="IBindTable"/> plus the binds that are visible only while the shell has asked.
/// An asked conditional wins over an always-on bind for the same chord. When two asked conditions
/// bind that chord, the one asked most recently wins. Asking it off reveals the bind underneath.
/// The lock is the one the hook and the shell share: resolve and <see cref="Ask"/> may run on different threads.
/// </summary>
public sealed class BindBook : IBindTable, IBindAsks
{
    private readonly IBindTable _inner;
    private readonly object _gate = new();
    private readonly List<Bind> _conditional = [];
    private readonly Dictionary<string, bool> _asked = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>Wraps <paramref name="inner"/>. Conditional binds added here sit over it.</summary>
    /// <param name="inner">The always-on binds. A reload replaces what this table resolves, and the conditionals stay.</param>
    public BindBook(IBindTable inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Adds a bind that is visible only while <see cref="Bind.While"/> is asked.</summary>
    /// <param name="bind">A bind with a condition. An always-on bind belongs on the table this book wraps.</param>
    public void Add(Bind bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        if (string.IsNullOrEmpty(bind.While))
        {
            throw new ArgumentException("A conditional bind names the condition it waits on.", nameof(bind));
        }

        lock (_gate)
        {
            _conditional.Add(bind);
        }
    }

    /// <inheritdoc />
    public void Ask(string condition, bool active)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        lock (_gate)
        {
            _asked[condition] = active;
            _order.Remove(condition);
            _order.Add(condition);
        }
    }

    /// <inheritdoc />
    public Dispatch? Resolve(Chord chord)
    {
        lock (_gate)
        {
            return Overlay(chord) is { } bind ? bind.To : _inner.Resolve(chord);
        }
    }

    /// <inheritdoc />
    public bool IsHold(Chord chord)
    {
        lock (_gate)
        {
            return Overlay(chord) is { } bind ? bind.Hold : _inner.IsHold(chord);
        }
    }

    /// <inheritdoc />
    public Dispatch? ResolveHold(Chord chord)
    {
        lock (_gate)
        {
            if (Overlay(chord) is { } bind)
            {
                return bind.Hold ? bind.To : null;
            }

            return _inner.ResolveHold(chord);
        }
    }

    private Bind? Overlay(Chord chord)
    {
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var condition = _order[i];
            if (_asked[condition] != true)
            {
                continue;
            }

            for (var b = _conditional.Count - 1; b >= 0; b--)
            {
                var bind = _conditional[b];
                if (bind.While == condition && bind.Chord == chord)
                {
                    return bind;
                }
            }
        }

        return null;
    }
}
