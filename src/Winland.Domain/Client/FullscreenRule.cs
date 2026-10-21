namespace Winland.Domain;

/// <summary>Internal and client modes from a <c>fullscreen_state</c> rule. The client mode is stored, not sent to the window.</summary>
public readonly record struct FullscreenRule(FullscreenMode Internal, FullscreenMode Client);
