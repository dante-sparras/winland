using System.Collections.Concurrent;

namespace Winland.Infrastructure;

/// <summary>
/// The windows the core declined, with the class each had and the config's rule revision when it was declined. A
/// new title offers such a window to the rules again only when it could change their answer, so a window that keeps
/// renaming itself is not described and matched on every change.
/// </summary>
internal sealed class DeclinedWindows
{
    private readonly ConcurrentDictionary<WindowId, (string Class, int Revision)> _declined = new();

    /// <summary>
    /// Remembers a window the core declined. A window with no title is not remembered: a title is what it lacks.
    /// </summary>
    public void Note(WindowId window, WindowTraits traits, int revision)
    {
        if (traits.Titled)
            _declined[window] = (traits.Class, revision);
        else
            Forget(window);
    }

    public void Forget(WindowId window) => _declined.TryRemove(window, out _);

    /// <summary>
    /// Whether a new title leaves the core's answer for <paramref name="window"/> as it was: the rules have no
    /// pattern on title, they are the ones that declined it, and its class (read only then) is the same.
    /// </summary>
    public bool Unchanged(WindowId window, bool titleRules, int revision, Func<string> classOf) =>
        !titleRules
        && _declined.TryGetValue(window, out var seen)
        && seen.Revision == revision
        && seen.Class == classOf();
}
