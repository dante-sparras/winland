using System.Globalization;
using System.Text.Json;

namespace Winland.Domain;

/// <summary>How one type of Option value is read from a config line, and written in a <c>getoption</c> reply.</summary>
internal sealed record OptionValue<T>(string Kind, OptionReader<T> Read, Func<T, string> Json);
