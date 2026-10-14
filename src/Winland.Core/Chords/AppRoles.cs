namespace Winland.Core.Chords;

/// <summary>One role a Host can name a program for.</summary>
/// <param name="Name">The role, as <c>apps</c> in <c>settings.json</c> is keyed by it, such as <c>music</c>.</param>
/// <param name="Summary">One sentence saying what its Chord opens. <c>docs/user/02-hotkeys.md</c> prints the same sentence.</param>
/// <param name="Chord">The key Omarchy's manual has it on, which the built-in map binds.</param>
public sealed record AppRoleRow(string Name, string Summary, Chord Chord)
{
    /// <summary>Its chord id in <c>bindings.json</c>: <c>app.</c> and the name.</summary>
    public string Id => AppRoles.Prefix + Name;
}

/// <summary>
/// The roles of the App chords, one for each program row of the manual's hotkey chapter that Winoma leaves to the Host.
/// A role is named for what its program is for, never for a program. The browser, the terminal, and the file manager are not roles:
/// they keep their own settings and Chords.
/// </summary>
public static class AppRoles
{
    /// <summary>What a role's chord id starts with.</summary>
    public const string Prefix = "app.";

    /// <summary>Every role, in the order <c>docs/user/02-hotkeys.md</c> lists them.</summary>
    public static IReadOnlyList<AppRoleRow> Rows { get; } =
    [
        Row("music", ChordModifiers.None, ChordKey.M, "Open the music player."),
        Row("music-2", ChordModifiers.Alt, ChordKey.M, "Open the second music player."),
        Row("passwords", ChordModifiers.None, ChordKey.Slash, "Open the password manager."),
        Row("editor", ChordModifiers.None, ChordKey.N, "Open the editor."),
        Row("calendar", ChordModifiers.None, ChordKey.C, "Open the calendar."),
        Row("email", ChordModifiers.None, ChordKey.E, "Open the email program."),
        Row("email-compose", ChordModifiers.Alt, ChordKey.E, "Write a new email."),
        Row("ai", ChordModifiers.None, ChordKey.A, "Open the AI assistant."),
        Row("ai-2", ChordModifiers.Alt, ChordKey.A, "Open the second AI assistant."),
        Row("messenger", ChordModifiers.None, ChordKey.G, "Open the messenger."),
        Row("messenger-2", ChordModifiers.Alt, ChordKey.G, "Open the second messenger."),
        Row("messenger-3", ChordModifiers.Ctrl, ChordKey.G, "Open the third messenger."),
        Row("photos", ChordModifiers.None, ChordKey.P, "Open the photo library."),
        Row("maps", ChordModifiers.None, ChordKey.S, "Open the map."),
        Row("containers", ChordModifiers.None, ChordKey.D, "Open the container manager."),
        Row("notes", ChordModifiers.None, ChordKey.O, "Open the notes program."),
        Row("writing", ChordModifiers.None, ChordKey.W, "Open the writing program."),
        Row("social", ChordModifiers.None, ChordKey.X, "Open the social network."),
        Row("social-compose", ChordModifiers.Alt, ChordKey.X, "Write a new post."),
        Row("video", ChordModifiers.None, ChordKey.Y, "Open the video program."),
    ];

    private static readonly Dictionary<string, AppRoleRow> ByName =
        Rows.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The role named <paramref name="name"/>, or null. Case is ignored.</summary>
    /// <param name="name">A role name, without <c>app.</c>.</param>
    /// <returns>The role, or null when there is none by that name.</returns>
    public static AppRoleRow? Find(string name) =>
        name is not null && ByName.TryGetValue(name, out var row) ? row : null;

    /// <summary>The role whose chord id is <paramref name="id"/>, or null. Case is ignored.</summary>
    /// <param name="id">A chord id, such as <c>app.music</c>.</param>
    /// <returns>The role, or null when the id names none.</returns>
    public static AppRoleRow? FromId(string id) =>
        id is not null && id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? Find(id[Prefix.Length..]) : null;

    // Every role in the manual is on Super and Shift. A row gives what it adds to those.
    private static AppRoleRow Row(string name, ChordModifiers more, ChordKey key, string summary) =>
        new(name, summary, new Chord(ChordModifiers.Super | ChordModifiers.Shift | more, key));
}
