namespace Winland.Domain;

public abstract record BindDirective;

public sealed record Bind(
    Modifier Modifiers,
    string Key,
    string Dispatcher,
    string Arguments,
    BindFlag Flags,
    string? Description) : BindDirective;

public sealed record Unbind(Modifier Modifiers, string Key) : BindDirective;

public sealed record UnbindAll : BindDirective;
