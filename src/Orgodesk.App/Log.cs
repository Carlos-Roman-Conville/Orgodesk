using Orgodesk.Core.Persistence;

namespace Orgodesk.App;

/// <summary>Appends errors to %APPDATA%\orgodesk\error.log. Never throws.</summary>
public static class Log
{
    public static string Path => System.IO.Path.Combine(LayoutStore.DefaultRoot, "error.log");

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(ex != null ? ": " + ex : "")}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Info(string message) => Error(message);
}
