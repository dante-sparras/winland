using Microsoft.Win32;
using Winland.Core;

namespace Winland.Windows;

/// <summary>Opens a file from the notification-area menu in the program Windows associates with its type.</summary>
public static class TrayFiles
{
    public static void Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("There is no file to open.");
            return;
        }

        var association = FileAssociation.OpenCommand(Path.GetExtension(path));
        _ = ProcessLauncher.TryLaunch(TrayOpener.Open(path, association));
    }
}

/// <summary>The open command Windows has stored for a file extension, when it has one.</summary>
internal static class FileAssociation
{
    internal static string? OpenCommand(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        try
        {
            return Lookup(extension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    // The user's choice wins over the type registration. Either one names a ProgId whose open command
    // carries the path as %1, %L or %*.
    private static string? Lookup(string extension)
    {
        var progId = UserChoice(extension) ?? Registered(extension);
        if (string.IsNullOrWhiteSpace(progId))
            return null;

        using var command = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
        return command?.GetValue(null) as string;
    }

    private static string? UserChoice(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + extension + @"\UserChoice");
        return key?.GetValue("ProgId") as string;
    }

    private static string? Registered(string extension)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(extension);
        return key?.GetValue(null) as string;
    }
}
