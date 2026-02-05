using System.Diagnostics;

namespace MauiTabbedModalLeakRepro;

internal static class ReproLog
{
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] LeakRepro | {message}";
        Debug.WriteLine(line);
        Console.WriteLine(line);
    }
}
