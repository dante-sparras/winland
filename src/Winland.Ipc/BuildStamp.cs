namespace Winland.Ipc;

/// <summary>
/// The protocol this assembly was compiled with. The handshake carries it, and a client that speaks another protocol is refused.
/// The value is a hash of the contract sources (<see cref="ProtocolStamp"/>), computed when this project builds.
/// </summary>
public static partial class BuildStamp
{
    /// <summary>The lowercase hex hash of the contract. Trimmed of the line the build script writes.</summary>
    public static string Value => Stamp.Trim();
}
