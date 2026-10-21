using System.Globalization;

namespace Winland.Domain;

/// <summary>How a focused column is brought into the viewport. <c>0</c> centers it. <c>1</c> fits it.</summary>
public enum ScrollingFit
{
    Center = 0,
    Fit = 1,
}
