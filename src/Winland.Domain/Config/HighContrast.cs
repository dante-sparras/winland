namespace Winland.Domain;

/// <summary>
/// The colors a Windows High Contrast theme gives selected items, text and unavailable text. While one is on, they
/// stand in for the default border and tab bar colors a config leaves unset, and the accent is not used.
/// </summary>
public readonly record struct HighContrast(BorderColor Highlight, BorderColor Text, BorderColor GrayText);
