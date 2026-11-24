using System.Security.Cryptography;
using System.Text;

namespace Orgodesk.Commands;

/// <summary>
/// Named events between command mode and the running app, one set per data folder (a test or VM copy with its own
/// folder never reaches the person's running Orgodesk). The app creates them at start and waits on them; command mode
/// only opens existing ones, so "the event exists" means "Orgodesk is running with this data folder".
/// </summary>
public sealed class DeskSignals
{
    private readonly string _prefix;

    public DeskSignals(string dataFolder)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataFolder).TrimEnd('\\').ToLowerInvariant())))[..16];
        _prefix = @"Local\Orgodesk." + id + ".";
    }

    /// <summary>layout.json was changed by a command: read it again.</summary>
    public string Reload => _prefix + "Reload";
    public string Hide => _prefix + "QuickHide";
    public string Show => _prefix + "QuickShow";

    public bool AppRunning()
    {
        if (!EventWaitHandle.TryOpenExisting(Reload, out var e)) return false;
        e.Dispose();
        return true;
    }

    /// <summary>Sets one of the app's events; false when the app is not running.</summary>
    public bool Send(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var e)) return false;
        using (e) e.Set();
        return true;
    }

    /// <summary>The app's side: the three events, created so command mode can find them.</summary>
    public EventWaitHandle[] Create() => new[] { Reload, Hide, Show }.Select(n => new EventWaitHandle(false, EventResetMode.AutoReset, n)).ToArray();
}
