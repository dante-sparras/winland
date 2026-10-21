using System.Globalization;

namespace Winland.Domain;

public enum FocusCause
{
    /// <summary>Winland moved the foreground itself.</summary>
    Requested,

    /// <summary>A click or a key preceded the change.</summary>
    User,

    /// <summary>The application took the foreground without a recent click or key.</summary>
    Activated,
}
