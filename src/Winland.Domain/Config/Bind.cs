namespace Winland.Domain;

public sealed record Bind(
    Modifier Modifiers,
    string Key,
    string Dispatcher,
    string Arguments,
    BindFlag Flags,
    string? Description,
    string? Submap,
    string? SubmapReset = null,
    IReadOnlyList<string>? Devices = null) : BindDirective;
