using System.Globalization;

namespace Winland.Domain;

/// <summary>A width and height in pixels. A null size on a window is no limit on that axis pair.</summary>
public readonly record struct Size(int Width, int Height);
