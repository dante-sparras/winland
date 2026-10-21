namespace Winland.Core;

/// <summary>Whether the interactive session is locked. The session phase is the live source.</summary>
public interface ISessionLock
{
    bool Locked { get; }
}
