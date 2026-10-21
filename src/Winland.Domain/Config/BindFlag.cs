namespace Winland.Domain;

[Flags]
public enum BindFlag
{
    None = 0,
    Locked = 1 << 0,
    Release = 1 << 1,
    Repeat = 1 << 2,
    Mouse = 1 << 3,
    NonConsuming = 1 << 4,
    Transparent = 1 << 5,
    IgnoreModifiers = 1 << 6,
    MultiKey = 1 << 7,
    LongPress = 1 << 8,
    DontInhibit = 1 << 9,
    Click = 1 << 10,
    Drag = 1 << 11,
    SubmapUniversal = 1 << 12,

    /// <summary>Swallow the key only when the dispatcher succeeds, as Winland's auto-consuming flag.</summary>
    AutoConsuming = 1 << 13,

    /// <summary>The bind matches only the devices it names. Without it, the bind matches every device but those.</summary>
    DeviceInclusive = 1 << 14,
}
