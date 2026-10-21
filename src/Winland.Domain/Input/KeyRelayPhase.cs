namespace Winland.Domain;

/// <summary>Whether a relayed key goes down, comes up, or goes down and up.</summary>
public enum KeyRelayPhase
{
    Press,
    Release,
    Tap,
}
