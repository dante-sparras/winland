using System.Globalization;

namespace Winland.Domain;

/// <summary>A menu row: the command it runs and the words the menu shows.</summary>
public readonly record struct TrayMenuItem(TrayCommand Command, string Label);
