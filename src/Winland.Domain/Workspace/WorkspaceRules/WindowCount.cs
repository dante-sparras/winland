using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Domain;

/// <summary>
/// What a <c>w[]</c> workspace selector counts: only tiled or only floating Clients when <see cref="Tiled"/>
/// is set, only pinned ones, Groups in place of Clients, and only the ones shown.
/// </summary>
internal readonly record struct WindowCount(bool? Tiled, bool Pinned, bool Groups, bool Visible);
