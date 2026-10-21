using System.Globalization;
using System.Text.Json;

namespace Winland.Domain;

/// <summary>An Option the config may set: its name, and whether a value reads as its type.</summary>
internal interface IOption
{
    string Name { get; }

    bool Reads(string text);
}
