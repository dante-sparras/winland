namespace Winland.Domain;

/// <summary>The key or button edge that ran a bind.</summary>
public readonly record struct BindTrigger(string Key, int Code, bool Pressed);
