namespace Winland.Domain;

/// <summary>A bind written as the config line that creates it, which the config parser reads back as the same bind.</summary>
public static class BindText
{
    public static string Line(Bind bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        var keyword = "bind" + Flags(bind);
        var fields = new List<string> { ModifierNames.Format(bind.Modifiers), bind.Key };
        if (bind.Description is not null)
            fields.Add(bind.Description);

        // A mouse bind has no argument field. Its action carries the argument, as in resizewindow 1.
        if (bind.Flags.HasFlag(BindFlag.Mouse) && bind.Arguments.Length > 0)
        {
            fields.Add(bind.Dispatcher + " " + bind.Arguments);
        }
        else
        {
            fields.Add(bind.Dispatcher);
            if (bind.Arguments.Length > 0)
                fields.Add(bind.Arguments);
        }

        // A # starts a comment in the config, and ## is a literal one.
        return keyword + " = " + string.Join(", ", fields).Replace("#", "##", StringComparison.Ordinal);
    }

    private static string Flags(Bind bind)
    {
        var flags = "";
        if (bind.Flags.HasFlag(BindFlag.Locked))
            flags += 'l';
        if (bind.Flags.HasFlag(BindFlag.Release))
            flags += 'r';
        if (bind.Flags.HasFlag(BindFlag.Repeat))
            flags += 'e';
        if (bind.Flags.HasFlag(BindFlag.Mouse))
            flags += 'm';
        if (bind.Flags.HasFlag(BindFlag.NonConsuming))
            flags += 'n';
        if (bind.Description is not null)
            flags += 'd';

        return flags;
    }
}
