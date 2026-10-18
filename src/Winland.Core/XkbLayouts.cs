namespace Winland;

/// <summary>A keyboard layout the config names: the Windows layout it loads, and the name the pinned release shows for it.</summary>
public readonly record struct KeyboardLayoutChoice(string Layout, string Variant, string? WindowsId, string Description);

/// <summary>
/// The XKB layout names the config uses, with the Windows keyboard layout that matches each and its XKB description. A
/// layout Windows has no match for keeps its place in the list but loads nothing.
/// </summary>
public static class XkbLayouts
{
    private static readonly Dictionary<string, (string Id, string Description)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = ("00000409", "English (US)"),
        ["us(intl)"] = ("00020409", "English (US, intl., with dead keys)"),
        ["us(dvorak)"] = ("00010409", "English (Dvorak)"),
        ["us(dvorak-l)"] = ("00030409", "English (Dvorak, left-handed)"),
        ["us(dvorak-r)"] = ("00040409", "English (Dvorak, right-handed)"),
        ["gb"] = ("00000809", "English (UK)"),
        ["gb(extd)"] = ("00000452", "English (UK, extended, Windows)"),
        ["ie"] = ("00001809", "Irish"),
        ["ca"] = ("00001009", "French (Canada)"),
        ["ca(multix)"] = ("00011009", "Canadian (CSA)"),
        ["de"] = ("00000407", "German"),
        ["at"] = ("00000407", "German (Austria)"),
        ["ch"] = ("00000807", "German (Switzerland)"),
        ["ch(fr)"] = ("0000100C", "French (Switzerland)"),
        ["fr"] = ("0000040C", "French"),
        ["fr(bepo)"] = ("0002040C", "French (BEPO)"),
        ["be"] = ("0000080C", "Belgian"),
        ["nl"] = ("00000413", "Dutch"),
        ["se"] = ("0000041D", "Swedish"),
        ["no"] = ("00000414", "Norwegian"),
        ["dk"] = ("00000406", "Danish"),
        ["fi"] = ("0000040B", "Finnish"),
        ["is"] = ("0000040F", "Icelandic"),
        ["ee"] = ("00000425", "Estonian"),
        ["lv"] = ("00000426", "Latvian"),
        ["lt"] = ("00000427", "Lithuanian"),
        ["pl"] = ("00000415", "Polish"),
        ["cz"] = ("00000405", "Czech"),
        ["sk"] = ("0000041B", "Slovak"),
        ["hu"] = ("0000040E", "Hungarian"),
        ["si"] = ("00000424", "Slovenian"),
        ["hr"] = ("0000041A", "Croatian"),
        ["rs"] = ("00000C1A", "Serbian"),
        ["rs(latin)"] = ("0000081A", "Serbian (Latin)"),
        ["ba"] = ("0000141A", "Bosnian"),
        ["ro"] = ("00010418", "Romanian"),
        ["bg"] = ("00030402", "Bulgarian"),
        ["mk"] = ("0000042F", "Macedonian"),
        ["al"] = ("0000041C", "Albanian"),
        ["gr"] = ("00000408", "Greek"),
        ["tr"] = ("0000041F", "Turkish"),
        ["tr(f)"] = ("0001041F", "Turkish (F)"),
        ["ua"] = ("00000422", "Ukrainian"),
        ["ru"] = ("00000419", "Russian"),
        ["by"] = ("00000423", "Belarusian"),
        ["kz"] = ("0000043F", "Kazakh"),
        ["ge"] = ("00000437", "Georgian"),
        ["am"] = ("0000042B", "Armenian"),
        ["az"] = ("0000042C", "Azerbaijani"),
        ["es"] = ("0000040A", "Spanish"),
        ["latam"] = ("0000080A", "Spanish (Latin American)"),
        ["pt"] = ("00000816", "Portuguese"),
        ["br"] = ("00000416", "Portuguese (Brazil)"),
        ["it"] = ("00000410", "Italian"),
        ["mt"] = ("0000043A", "Maltese"),
        ["il"] = ("0000040D", "Hebrew"),
        ["ara"] = ("00000401", "Arabic"),
        ["ir"] = ("00000429", "Persian"),
        ["in"] = ("00004009", "Indian"),
        ["pk"] = ("00000420", "Urdu (Pakistan)"),
        ["th"] = ("0000041E", "Thai"),
        ["vn"] = ("0000042A", "Vietnamese"),
        ["jp"] = ("00000411", "Japanese"),
        ["kr"] = ("00000412", "Korean"),
        ["cn"] = ("00000804", "Chinese"),
        ["tw"] = ("00000404", "Taiwanese"),
        ["mn"] = ("00000450", "Mongolian"),
    };

    /// <summary>
    /// The layouts of a comma-separated <c>kb_layout</c>, each with the variant at the same place in <c>kb_variant</c>.
    /// A variant Windows has no layout for falls back to the plain layout.
    /// </summary>
    public static IReadOnlyList<KeyboardLayoutChoice> Parse(string layouts, string variants)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(variants);
        var names = layouts.Split(',', StringSplitOptions.TrimEntries);
        var kinds = variants.Split(',', StringSplitOptions.TrimEntries);
        var choices = new List<KeyboardLayoutChoice>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            var layout = names[i];
            if (layout.Length == 0)
                continue;

            var variant = i < kinds.Length ? kinds[i] : "";
            if (variant.Length > 0 && Known.TryGetValue($"{layout}({variant})", out var exact))
                choices.Add(new KeyboardLayoutChoice(layout, variant, exact.Id, exact.Description));
            else if (Known.TryGetValue(layout, out var plain))
                choices.Add(new KeyboardLayoutChoice(layout, variant, plain.Id, plain.Description));
            else
                choices.Add(new KeyboardLayoutChoice(layout, variant, null, layout));
        }

        return choices;
    }
}
