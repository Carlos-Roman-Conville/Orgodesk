using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Orgodesk.App;

/// <summary>
/// Remembers, outside the data folder, that this Windows account already activated a key (FC-40). Gumroad's activation
/// counter is global and counts every activation, so without this a reinstall that deletes the data folder would use
/// another of the buyer's activations on the same PC. Only a SHA-256 hash of the key is stored, in
/// HKCU\Software\Orgodesk; nothing is sent anywhere.
/// </summary>
internal static class ActivationMarker
{
    private const string KeyPath = @"Software\Orgodesk";
    private const string ValueName = "Activated";

    private static string Hash(string licenseKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(licenseKey.Trim().ToUpperInvariant())));

    public static bool Has(string licenseKey)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue(ValueName) is string s && s.Equals(Hash(licenseKey), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Set(string licenseKey)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k?.SetValue(ValueName, Hash(licenseKey));
        }
        catch (Exception ex) { Log.Error("Could not remember the activation", ex); }
    }
}
