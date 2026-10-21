namespace Winland.Domain;

/// <summary>Which part of a window's picture is drawn where, both in pixels of the stand-in's own coordinates.</summary>
public readonly record struct StandInMapping(Rectangle Source, Rectangle Destination);
