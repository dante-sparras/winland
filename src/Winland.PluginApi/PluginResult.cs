namespace Winland.PluginApi;

/// <summary>
/// The result of a plugin dispatcher. <see cref="Value"/> is reported on success.
/// <see cref="Error"/> is reported on failure.
/// </summary>
public readonly record struct PluginResult(bool Ok, string? Value = null, string? Error = null)
{
    public static PluginResult Success(string? value = null) => new(true, value, null);

    public static PluginResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(false, null, error);
    }
}
