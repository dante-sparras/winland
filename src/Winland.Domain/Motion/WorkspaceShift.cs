using System.Globalization;

namespace Winland.Domain;

/// <summary>How far a Workspace's windows travel and whether they fade, for one animation style.</summary>
internal readonly record struct WorkspaceShift(int Dx, int Dy, bool Fades);
