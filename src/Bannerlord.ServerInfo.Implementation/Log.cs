using System;

using TaleWorlds.Library;

namespace Bannerlord.ServerInfo;

/// <summary>
/// Provides unified logging to both the dedicated server console and the game debug log.
/// </summary>
internal static class Log
{
    private const string Prefix = "[ServerInfo] ";

    public static void Info(string message) => Write(message, Debug.DebugColor.Green);

    public static void Error(string message) => Write(message, Debug.DebugColor.Red);

    private static void Write(string message, Debug.DebugColor color)
    {
        Console.WriteLine(Prefix + message);
        Debug.Print(Prefix + message, 0, color);
    }
}
