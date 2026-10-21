namespace Winland.Domain;

/// <summary>How a window left its Group.</summary>
internal enum GroupExit
{
    /// <summary>The window was not a member.</summary>
    NotMember,

    /// <summary>It was the last member. The Group ended and the window keeps its tile.</summary>
    Dissolved,

    /// <summary>It left a Group that keeps other members. It is out of the Layout now.</summary>
    Detached,
}
