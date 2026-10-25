using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

/// <summary>One consistent bind table. Published as a whole on config load and reload.</summary>
public sealed class BindTable
{
    public static BindTable Empty { get; } = new([], scrollDelay: 300);

    private BindTable(Bind[] entries, int scrollDelay)
    {
        Binds = Array.AsReadOnly(entries);
        ScrollDelay = scrollDelay;
    }

    public IReadOnlyList<Bind> Binds { get; }

    internal int ScrollDelay { get; }

    internal static BindTable From(ConfigDocument config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var binds = new List<Bind>();
        foreach (var directive in config.Binds)
        {
            switch (directive)
            {
                case Bind bind:
                    binds.Add(bind);
                    break;
                case Unbind unbind:
                    binds.RemoveAll(bind => bind.Modifiers == unbind.Modifiers && Is(bind.Key, unbind.Key));
                    break;
                case UnbindAll:
                    binds.Clear();
                    break;
            }
        }

        return new BindTable([.. binds], BindOptions.From(config).ScrollEventDelay);
    }
}
