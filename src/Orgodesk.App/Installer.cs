using System.Diagnostics;
using System.Windows.Forms;
using Orgodesk.Core.Persistence;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App;

/// <summary>
/// Self-install: copies this exe to a stable per-user folder, registers it to start with Windows,
/// adds a Start Menu shortcut and launches it. No admin rights. --uninstall undoes the registration.
/// </summary>
public static class Installer
{
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Orgodesk");
    public static string InstalledExe => Path.Combine(InstallDir, "Orgodesk.exe");
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Orgodesk.lnk");

    public static int Install()
    {
        try
        {
            StopRunningInstance();
            RemoveLegacyInstall();
            var self = Environment.ProcessPath ?? Application.ExecutablePath;
            Directory.CreateDirectory(InstallDir);
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, InstalledExe, overwrite: true);

            AutoStart.SetEnabled(true, InstalledExe);
            var store = new LayoutStore(LayoutStore.DefaultRoot);
            var settings = store.LoadSettings();
            settings.StartWithWindows = true;
            store.SaveSettings(settings);
            CreateShortcut(ShortcutPath, InstalledExe, "Orgodesk");

            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

            string note = StardockFencesAutostarts()
                ? "\n\nNote: Stardock Fences is also set to start with Windows. Remove it from startup (Task Manager > Startup apps) or the two will fight over the desktop."
                : "";
            MessageBox.Show($"Installed to:\n{InstalledExe}\n\nIt now starts with Windows and is running. Double right-click the desktop to switch it off and on; the tray icon has settings and exit.{note}",
                "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            MessageBox.Show("Install failed: " + ex.Message, "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static int Uninstall()
    {
        try
        {
            StopRunningInstance();
            AutoStart.SetEnabled(false, InstalledExe);
            var store = new LayoutStore(LayoutStore.DefaultRoot);
            var settings = store.LoadSettings();
            settings.StartWithWindows = false;
            store.SaveSettings(settings);
            try { File.Delete(ShortcutPath); } catch { }
            DesktopWindows.SetDesktopIconsVisible(true);
            MessageBox.Show($"Orgodesk no longer starts with Windows. The program file stays at\n{InstalledExe}\nand your layout stays in\n{LayoutStore.DefaultRoot}\nDelete those folders by hand if you want them gone.",
                "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Uninstall failed", ex);
            MessageBox.Show("Uninstall failed: " + ex.Message, "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void StopRunningInstance()
    {
        bool any = false;
        foreach (var name in new[] { Program.ExitEventName, Program.LegacyExitEventName })
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var ev)) continue;
            any = true;
            using (ev) ev.Set();
            for (int i = 0; i < 40 && EventWaitHandle.TryOpenExisting(name, out var still); i++)
            {
                still.Dispose();
                Thread.Sleep(250);
            }
        }
        if (any) Thread.Sleep(500);
    }

    /// <summary>Removes the registration and files of the program's earlier name (Fences Clone).</summary>
    private static void RemoveLegacyInstall()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                k?.DeleteValue("FencesClone", throwOnMissingValue: false);
            var oldShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Fences Clone.lnk");
            if (File.Exists(oldShortcut)) File.Delete(oldShortcut);
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FencesClone");
            if (Directory.Exists(oldDir)) Directory.Delete(oldDir, recursive: true);
        }
        catch (Exception ex) { Log.Error("Legacy cleanup", ex); }
    }

    private static bool StardockFencesAutostarts()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return k?.GetValue("Fences") is string;
        }
        catch { return false; }
    }

    private static void CreateShortcut(string path, string target, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.Description = description;
            lnk.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Shortcut failed", ex);
        }
    }
}
