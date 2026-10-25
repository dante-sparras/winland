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
}
