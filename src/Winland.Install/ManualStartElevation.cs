namespace Winland.Install;

public static class ManualStartElevation
{
    // WINLAND_NO_ELEVATE=1 starts without elevation. It exists for tests.
    // Elevated features then do not work. --no-elevate is the same opt-out.
    public const string OptOutVariable = "WINLAND_NO_ELEVATE";

    public const string LoopArgument = "--winland-elevated";

    public const string OptOutArgument = "--no-elevate";

    public static ElevationChoice Decide(
        bool isElevated,
        string? optOutVariable,
        IReadOnlyList<string> arguments,
        bool userInstall = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var kept = arguments.Where(argument => !IsLoopArgument(argument) && !IsOptOutArgument(argument)).ToArray();
        // A per-user install never relaunches through UAC, including a start that did not pass --no-elevate.
        if (isElevated || IsOptOut(optOutVariable) || arguments.Any(IsOptOutArgument) || userInstall)
            return new ElevationChoice.Continue(kept);

        // The elevated process is started with the loop argument. If that process
        // is still not elevated, relaunching again would ask for approval forever.
        if (arguments.Any(IsLoopArgument))
            return new ElevationChoice.Refuse("Winland could not restart with administrator rights.");

        return new ElevationChoice.Relaunch([LoopArgument, .. kept]);
    }

    private static bool IsOptOut(string? variable) =>
        string.Equals(variable?.Trim(), "1", StringComparison.Ordinal);

    private static bool IsLoopArgument(string value) =>
        value.Equals(LoopArgument, StringComparison.OrdinalIgnoreCase);

    private static bool IsOptOutArgument(string value) =>
        value.Equals(OptOutArgument, StringComparison.OrdinalIgnoreCase);
}

public abstract record ElevationChoice
{
    private ElevationChoice()
    {
    }

    public sealed record Continue(IReadOnlyList<string> Arguments) : ElevationChoice;

    public sealed record Relaunch(IReadOnlyList<string> Arguments) : ElevationChoice;

    public sealed record Refuse(string Message) : ElevationChoice;
}
