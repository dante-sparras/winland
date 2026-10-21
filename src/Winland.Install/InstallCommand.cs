namespace Winland.Install;

/// <summary>What <c>winland install</c>, <c>winland update</c>, <c>winland tweaks</c> and <c>winland uninstall</c> were asked to do.</summary>
public abstract record InstallCommand
{
    private InstallCommand()
    {
    }

    /// <summary>The elevated install. <paramref name="Account"/> is the user the relaunch carried, when it has happened.</summary>
    public sealed record Machine(string? Account) : InstallCommand;

    /// <summary>The per-user install. It asks for no UAC approval.</summary>
    public sealed record ForUser : InstallCommand;

    public sealed record Remove(bool Purge) : InstallCommand;

    /// <summary>
    /// Replace the install this process was started from with the newest published release.
    /// A machine install asks for one approval when a replace will happen. A per-user install does not.
    /// </summary>
    public sealed record Update : InstallCommand;

    /// <summary>
    /// Turn off Snap windows, and the precision touchpad swipes when <paramref name="Touchpad"/> is true.
    /// This stays a separate command because <c>winland install</c> may relaunch elevated, and these
    /// settings belong to the user who answered, not to an administrator who approved that prompt.
    /// </summary>
    public sealed record Tweaks(bool Touchpad) : InstallCommand;

    /// <summary>Null when the arguments are not an install, an update, a tweak or an uninstall.</summary>
    public static InstallCommand? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            return null;

        if (Is(args[0], "update"))
        {
            if (args.Count == 1)
                return new Update();

            throw new ArgumentException("usage: winland update");
        }

        if (Is(args[0], "tweaks"))
        {
            if (args.Count == 2 && Is(args[1], "--off"))
                return new Tweaks(false);

            if (args.Count == 3 && Is(args[1], "--off") && Is(args[2], "--touchpad"))
                return new Tweaks(true);

            throw new ArgumentException("usage: winland tweaks --off [--touchpad]");
        }

        if (!Is(args[0], "install") && !Is(args[0], "uninstall"))
            return null;

        if (Is(args[0], "install"))
        {
            if (args.Count == 1)
                return new Machine(null);

            if (args.Count == 2 && Is(args[1], "--user"))
                return new ForUser();

            // The elevated relaunch carries the account that invoked install. An over-the-shoulder
            // approval runs as the administrator who approved it, which is not the account that logs on.
            if (args.Count == 2 && !args[1].StartsWith('-'))
                return new Machine(args[1]);

            throw new ArgumentException("usage: winland install [--user]");
        }

        if (args.Count == 1)
            return new Remove(false);

        if (args.Count == 2 && Is(args[1], "--purge"))
            return new Remove(true);

        throw new ArgumentException("usage: winland uninstall [--purge]");
    }

    /// <summary>
    /// The elevated install always needs an administrator. Uninstall needs one only when a machine
    /// install is present, because deleting it does. The per-user install never does. Update decides
    /// later, and only when the running copy is the machine install and a replace will happen.
    /// </summary>
    public bool RequiresAdministrator(bool machineInstallPresent) => this switch
    {
        Machine => true,
        ForUser => false,
        Remove => machineInstallPresent,
        Update => false,
        Tweaks => false,
        _ => false,
    };

    private static bool Is(string value, string verb) =>
        value.Equals(verb, StringComparison.OrdinalIgnoreCase);
}
