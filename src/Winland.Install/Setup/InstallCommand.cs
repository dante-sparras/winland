namespace Winland.Install;

/// <summary>What <c>winland install</c>, <c>winland update</c>, <c>winland tweaks</c> and <c>winland uninstall</c> were asked to do.</summary>
public abstract record InstallCommand
{
    private InstallCommand()
    {
    }

    /// <summary>
    /// The elevated install. <paramref name="Account"/> is the user the relaunch carried, when it has happened.
    /// <paramref name="Logon"/> false is <c>--no-logon</c>, and <paramref name="Start"/> false is <c>--no-start</c>.
    /// </summary>
    public sealed record Machine(string? Account, bool Logon = true, bool Start = true) : InstallCommand
    {
        /// <summary>The arguments the elevated relaunch runs, carrying <paramref name="account"/> and these flags.</summary>
        public IReadOnlyList<string> Relay(string account) =>
            ["install", account, .. Flags(Logon, Start)];
    }

    /// <summary>The per-user install. It asks for no UAC approval.</summary>
    public sealed record ForUser(bool Logon = true, bool Start = true) : InstallCommand;

    // The bootstrap has no unelevated winland parent. It completes user cleanup through the installed copy.
    public sealed record UserCleanup : InstallCommand;

    public sealed record Remove(bool Purge) : InstallCommand;

    /// <summary>
    /// Replace the install this process was started from with the release of one channel.
    /// A null <see cref="Channel"/> means the saved channel, or stable when none is saved.
    /// A machine install asks for one approval when a replace will happen. A per-user install does not.
    /// </summary>
    public sealed record Update(UpdateChannel? Channel = null) : InstallCommand;

    /// <summary>
    /// Turn off Snap windows, and the precision touchpad swipes when <paramref name="Touchpad"/> is true.
    /// This stays a separate command because <c>winland install</c> may relaunch elevated, and these
    /// settings belong to the user who answered, not to an administrator who approved that prompt.
    /// </summary>
    public sealed record Tweaks(bool Touchpad) : InstallCommand;

    /// <summary>Null when the arguments are not an install, an update, a tweak or an uninstall.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out InstallCommand? command, out string? error)
    {
        try
        {
            command = Parse(args);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            command = null;
            error = exception.Message;
            return false;
        }
    }

    public static InstallCommand? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            return null;

        if (Is(args[0], "update"))
        {
            if (args.Count == 1)
                return new Update();

            if (args.Count == 3 && Is(args[1], "--channel"))
                return new Update(ParseChannel(args[2]));

            throw new ArgumentException("usage: winland update [--channel <stable|dev>]");
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
            if (args.Count == 2 && Is(args[1], "--winland-cleanup-user"))
                return new UserCleanup();
            return ParseInstall(args);
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

    private static InstallCommand ParseInstall(IReadOnlyList<string> args)
    {
        const string usage = "usage: winland install [--user] [--no-logon] [--no-start]";
        string? account = null;
        bool user = false, logon = true, start = true;
        // The elevated relaunch carries the account that invoked install, first after install. An over-the-shoulder
        // approval runs as the administrator who approved it, which is not the account that logs on.
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (Is(arg, "--user") && !user)
                user = true;
            else if (Is(arg, "--no-logon") && logon)
                logon = false;
            else if (Is(arg, "--no-start") && start)
                start = false;
            else if (i == 1 && !arg.StartsWith('-'))
                account = arg;
            else
                throw new ArgumentException(usage);
        }

        if (user && account is not null)
            throw new ArgumentException(usage);

        return user ? new ForUser(logon, start) : new Machine(account, logon, start);
    }

    private static IEnumerable<string> Flags(bool logon, bool start)
    {
        if (!logon)
            yield return "--no-logon";
        if (!start)
            yield return "--no-start";
    }

    private static UpdateChannel ParseChannel(string value)
    {
        if (value.Equals("stable", StringComparison.OrdinalIgnoreCase))
            return UpdateChannel.Stable;

        if (value.Equals("dev", StringComparison.OrdinalIgnoreCase))
            return UpdateChannel.Dev;

        throw new ArgumentException("usage: winland update [--channel <stable|dev>]");
    }

    private static bool Is(string value, string verb) =>
        value.Equals(verb, StringComparison.OrdinalIgnoreCase);
}
