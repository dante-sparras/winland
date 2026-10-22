namespace Winland.Domain;

/// <summary>How a window property's value is read, written and shown.</summary>
internal enum PropKind
{
    Bool,
    Int,
    Float,
    Text,
    Size,
    Alpha,
    AlphaOverride,
    Color,
}
