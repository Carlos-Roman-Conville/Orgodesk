using System.Runtime.InteropServices;

namespace Orgodesk.Windows.Shell;

/// <summary>The person's own folders by their English names, wherever they were moved to (SHGetKnownFolderPath).</summary>
public static class KnownFolders
{
    private static readonly Dictionary<string, Guid> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["Documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        ["Downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
        ["Music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        ["Pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["Videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
    };

    /// <summary>The folder's path, or null for a name that is not one of these or a folder Windows cannot give.</summary>
    public static string? Path(string name)
    {
        if (!Ids.TryGetValue(name.Trim(), out var id)) return null;
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
