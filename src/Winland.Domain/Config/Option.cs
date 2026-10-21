using System.Globalization;
using System.Text.Json;

namespace Winland.Domain;

/// <summary>One Option of a record of options: its name, its value type, and the field of the record it sets.</summary>
internal sealed class Option<TOptions> : IOption
    where TOptions : class
{
    private readonly Func<string, bool> _reads;
    private readonly Func<TOptions, string, TOptions?> _apply;
    private readonly Func<TOptions, string> _reply;

    private Option(string name, Func<string, bool> reads, Func<TOptions, string, TOptions?> apply, Func<TOptions, string> reply)
    {
        Name = name;
        _reads = reads;
        _apply = apply;
        _reply = reply;
    }

    public string Name { get; }

    public static Option<TOptions> Of<T>(string name, OptionValue<T> value, Func<TOptions, T> get, Func<TOptions, T, TOptions> set)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<TOptions>(
            name,
            text => value.Read(text, out _),
            (options, text) => value.Read(text, out var read) ? set(options, read) : null,
            options => "\"" + value.Kind + "\":" + value.Json(get(options)));
    }

    public bool Reads(string text) => _reads(text);

    /// <summary><paramref name="options"/> with this Option set to <paramref name="text"/>, or null when the value does not read.</summary>
    public TOptions? Apply(TOptions options, string text) => _apply(options, text);

    /// <summary>The value's type and the value in <paramref name="options"/>, as <c>"int":4</c>.</summary>
    public string Reply(TOptions options) => _reply(options);
}
