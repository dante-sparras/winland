namespace Winland.Domain;

internal readonly record struct MinimizedPresence(WindowId Window, bool WorkspaceShown, bool Minimized);
