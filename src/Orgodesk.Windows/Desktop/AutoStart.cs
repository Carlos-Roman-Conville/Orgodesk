using Microsoft.Win32;

namespace Orgodesk.Windows.Desktop;

/// <summary>Start-with-Windows through the per-user Run key. No admin rights needed.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Orgodesk";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (k == null) return;
        if (enabled) k.SetValue(ValueName, "\"" + exePath + "\"");
        else k.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
