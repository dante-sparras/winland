using System.Globalization;
using System.Text.Json;

namespace Winland.Domain;

internal delegate bool OptionReader<T>(string text, out T value);
