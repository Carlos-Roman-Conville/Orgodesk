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
            RegisterUninstallEntry();

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

    public static int Uninstall(bool quiet = false)
    {
        try
        {
            StopRunningInstance();
            AutoStart.SetEnabled(false, InstalledExe);
            try { File.Delete(ShortcutPath); } catch { }
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
            DesktopWindows.SetDesktopIconsVisible(true);
            TrayContext_ClearHiddenFlag();

            // Data: kept by default so a reinstall brings the Orgos back. Asked unless --quiet.
            bool deleteData = !quiet && MessageBox.Show(
                $"Also delete your Orgos, presets and settings?\n\nYes: delete {LayoutStore.DefaultRoot}\nNo: keep them, so reinstalling Orgodesk brings everything back.",
                "Uninstall Orgodesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (deleteData)
            {
                try { Directory.Delete(LayoutStore.DefaultRoot, recursive: true); } catch (Exception ex) { Log.Error("Could not delete data folder", ex); }
            }
            else
            {
                var store = new LayoutStore(LayoutStore.DefaultRoot);
                var settings = store.LoadSettings();
                settings.StartWithWindows = false;
                store.SaveSettings(settings);
            }

            Log.Info($"Uninstalled (data {(deleteData ? "deleted" : "kept")})");
            if (!quiet)
                MessageBox.Show(deleteData ? "Orgodesk has been removed, with your Orgos and settings." :
                    $"Orgodesk has been removed. Your Orgos and settings are kept in\n{LayoutStore.DefaultRoot}\nand come back if you reinstall.",
                    "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DeleteProgramFolderAfterExit();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Uninstall failed", ex);
            if (!quiet) MessageBox.Show("Uninstall failed: " + ex.Message, "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>
    /// The program folder may hold the exe that is running now (Settings > Apps starts the installed copy), so a hidden
    /// command deletes it after this process exits: it retries about once a second for 30 seconds until the folder is gone.
    /// Started last, after every dialog, so the exe is about to exit.
    /// </summary>
    private static void DeleteProgramFolderAfterExit()
    {
        if (!Directory.Exists(InstallDir)) return;
        var d = InstallDir;
        var loop = $"/c for /l %i in (1,1,30) do @(ping -n 2 127.0.0.1 >nul & rmdir /s /q \"{d}\" 2>nul & if not exist \"{d}\" exit /b 0)";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", loop)
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                  // Not inside the folder being deleted: Windows won't remove a folder a process is using as its working directory.
                  WorkingDirectory = Path.GetTempPath() });
        }
        catch (Exception ex) { Log.Error("Could not schedule the program folder removal", ex); }
    }

    private static void TrayContext_ClearHiddenFlag() => UI.TrayContext.ClearHiddenFlag();

    /// <summary>Per-user entry in Settings > Apps (no admin rights), so Orgodesk can be removed like any app.</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Orgodesk";

    private static void RegisterUninstallEntry()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey);
            if (k == null) return;
            var version = typeof(Installer).Assembly.GetName().Version;
            k.SetValue("DisplayName", "Orgodesk");
            k.SetValue("DisplayVersion", version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
            k.SetValue("Publisher", "Orgodesk");
            k.SetValue("DisplayIcon", InstalledExe);
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            k.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
            k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), Microsoft.Win32.RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        catch (Exception ex) { Log.Error("Could not register the uninstall entry", ex); }
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
