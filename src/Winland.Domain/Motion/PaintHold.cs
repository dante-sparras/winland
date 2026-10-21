namespace Winland.Domain;

/// <summary>Whether a running motion owns a window's alpha or border, so a decoration must not set them.</summary>
public readonly record struct PaintHold(bool Alpha, bool Border);
