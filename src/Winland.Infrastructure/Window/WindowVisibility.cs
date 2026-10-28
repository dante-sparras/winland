using Windows.Win32;

namespace Winland.Infrastructure;

/// <summary>
/// What a window that is shown, hidden, cloaked, uncloaked, created or renamed means for the core, from what the
/// window says about itself when the event arrives.
/// </summary>
internal static class WindowVisibility
{
    public static VisibilityAction Decide(uint @event, WindowFacts facts) => @event switch
    {
        // A minimize Winland did not ask for is taken back. An application that hides its own window unmaps it, and
        // it is adopted again when shown. One Winland hid, or one shown again before this event arrives, stays.
        PInvoke.EVENT_OBJECT_HIDE when !facts.Concealed && !facts.Visible && facts.Managed => VisibilityAction.Close,
        PInvoke.EVENT_OBJECT_HIDE when facts.Minimized && facts.Visible => VisibilityAction.UndoMinimize,
        PInvoke.EVENT_OBJECT_HIDE => VisibilityAction.None,

        // A virtual desktop switch cloaks hidden Clients too. A hidden window is not adopted again when it is
        // uncloaked, so it stays a Client and keeps its Workspace.
        PInvoke.EVENT_OBJECT_CLOAKED when !facts.Visible && facts.Managed => VisibilityAction.None,
        PInvoke.EVENT_OBJECT_CLOAKED => VisibilityAction.Close,

        // A Client's new title goes to the core. Restoring a window the core does not manage does nothing, so a
        // window that is not a Client is only offered for adoption.
        PInvoke.EVENT_OBJECT_SHOW or PInvoke.EVENT_OBJECT_UNCLOAKED or PInvoke.EVENT_OBJECT_NAMECHANGE or PInvoke.EVENT_OBJECT_CREATE
            when facts.Minimized => VisibilityAction.None,
        PInvoke.EVENT_OBJECT_NAMECHANGE when facts.Managed => VisibilityAction.Retitle,
        PInvoke.EVENT_OBJECT_SHOW or PInvoke.EVENT_OBJECT_UNCLOAKED or PInvoke.EVENT_OBJECT_NAMECHANGE or PInvoke.EVENT_OBJECT_CREATE
            when facts.Managed => VisibilityAction.Restore,
        // A window the core declined has no new answer in a new title, unless a rule reads titles or its class changed.
        PInvoke.EVENT_OBJECT_NAMECHANGE when facts.Declined => VisibilityAction.None,
        PInvoke.EVENT_OBJECT_SHOW or PInvoke.EVENT_OBJECT_UNCLOAKED or PInvoke.EVENT_OBJECT_NAMECHANGE or PInvoke.EVENT_OBJECT_CREATE
            => VisibilityAction.Adopt,
        _ => VisibilityAction.None,
    };
}

/// <summary>
/// What a window says about itself: minimized, visible, a Client, and hidden by Winland. Declined is a window the core
/// declined whose new title cannot change that.
/// </summary>
internal readonly record struct WindowFacts(bool Minimized, bool Visible, bool Managed, bool Concealed, bool Declined = false);

internal enum VisibilityAction
{
    None,

    /// <summary>Restore a window minimized without Winland.</summary>
    UndoMinimize,

    /// <summary>The window stops being a Client.</summary>
    Close,

    /// <summary>A Client's title changed.</summary>
    Retitle,

    /// <summary>A Client is shown again, then offered for adoption.</summary>
    Restore,

    /// <summary>A window that is not a Client is offered for adoption.</summary>
    Adopt,
}
