namespace Winland.Core.Binds;

/// <summary>Condition names a bind can wait on. The shell asks for one; Winland does not decide why.</summary>
public static class BindConditions
{
    /// <summary>Active while the shell has asked to take Escape.</summary>
    public const string Escape = "escape";
}
