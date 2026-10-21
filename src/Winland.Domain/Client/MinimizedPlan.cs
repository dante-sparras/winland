namespace Winland.Domain;

internal readonly record struct MinimizedPlan(
    IReadOnlyList<WindowId> Hide,
    IReadOnlyList<WindowId> Show,
    IReadOnlyList<WindowId> Concealed);
