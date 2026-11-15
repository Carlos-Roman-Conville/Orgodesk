using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orgodesk.Core.Licensing;

/// <summary>What is stored on disk about the trial and the license key.</summary>
public sealed class LicenseRecord
{
    /// <summary>First time this copy ran. The trial counts from here.</summary>
    public DateTime FirstRunUtc { get; set; }
    /// <summary>Latest time the app has seen. Guards against setting the clock back.</summary>
    public DateTime LastSeenUtc { get; set; }
    public string? Key { get; set; }
    /// <summary>Last time Gumroad confirmed the key. Null until the first activation succeeds.</summary>
    public DateTime? LastVerifiedUtc { get; set; }
    /// <summary>Set when Gumroad says the key is unknown, refunded or charged back.</summary>
    public bool Revoked { get; set; }
}

public enum LicenseState { DevBuild, Licensed, Trial, Expired }

public sealed record LicenseStatus(LicenseState State, int TrialDaysLeft, string Message)
{
    public bool CanRun => State != LicenseState.Expired;
}

/// <summary>Sales settings for one release. Empty product id = a development build with no license check.</summary>
public sealed record SalesConfig(string ProductId, string BuyUrl, int TrialDays = 14, int OfflineGraceDays = 30,
    int RecheckDays = 7, int MaxActivations = 5)
{
    public bool IsDevBuild => string.IsNullOrWhiteSpace(ProductId);
}

/// <summary>Pure rules: given the record, the config and the time, what state is this copy in.</summary>
public static class LicenseRules
{
    /// <summary>The time used for every decision: never earlier than the latest time already seen.</summary>
    public static DateTime EffectiveNow(LicenseRecord r, DateTime nowUtc) => nowUtc < r.LastSeenUtc ? r.LastSeenUtc : nowUtc;

    public static LicenseStatus Evaluate(LicenseRecord r, SalesConfig cfg, DateTime nowUtc)
    {
        if (cfg.IsDevBuild) return new LicenseStatus(LicenseState.DevBuild, 0, "Development build (no license check)");
        var now = EffectiveNow(r, nowUtc);

        if (!r.Revoked && !string.IsNullOrWhiteSpace(r.Key) && r.LastVerifiedUtc is DateTime v
            && (v - nowUtc).TotalDays <= cfg.OfflineGraceDays && (now - v).TotalDays <= cfg.OfflineGraceDays)
            return new LicenseStatus(LicenseState.Licensed, 0, "Licensed");

        // A trial start later than the real clock is an edited file: count the trial as used up.
        var used = InFuture(r.FirstRunUtc, nowUtc) ? cfg.TrialDays : (now - r.FirstRunUtc).TotalDays;
        int left = (int)Math.Ceiling(cfg.TrialDays - used);
        if (left > 0)
            return new LicenseStatus(LicenseState.Trial, left, left == 1 ? "Trial: 1 day left" : $"Trial: {left} days left");

        string why = r.Revoked ? "This license key is no longer valid."
            : !string.IsNullOrWhiteSpace(r.Key) && r.LastVerifiedUtc != null ? "Orgodesk could not check your license for a while. Connect to the internet and check it again."
            : "The free trial has ended.";
        return new LicenseStatus(LicenseState.Expired, 0, why);
    }

    /// <summary>True when a stored key should be confirmed with Gumroad again.</summary>
    public static bool RecheckDue(LicenseRecord r, SalesConfig cfg, DateTime nowUtc) =>
        !cfg.IsDevBuild && !string.IsNullOrWhiteSpace(r.Key) && !r.Revoked
        && (r.LastVerifiedUtc is not DateTime v || InFuture(v, nowUtc) || (EffectiveNow(r, nowUtc) - v).TotalDays >= cfg.RecheckDays);

    // Compared with the real clock, not EffectiveNow: an edited LastSeenUtc must not make a future date look valid.
    private static bool InFuture(DateTime t, DateTime nowUtc) => t > nowUtc.AddDays(1);
}

/// <summary>
/// A copy of the trial start kept outside the data folder (the App keeps it in HKCU\Software\Orgodesk), so Core stays
/// free of Windows code. Read returns null when nothing is stored.
/// </summary>
public interface ITrialMarker
{
    DateTime? Read();
    void Write(DateTime firstRunUtc);
}

/// <summary>Reads and writes license.json in the app's data folder.</summary>
public sealed class LicenseStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string Path { get; }

    private readonly ITrialMarker? _marker;

    public LicenseStore(string rootDir, ITrialMarker? marker = null)
    {
        Path = System.IO.Path.Combine(rootDir, "license.json");
        _marker = marker;
    }

    /// <summary>
    /// Loads the record, creating it on the first run. The trial start is the earlier of the file and the marker, so
    /// deleting or editing license.json does not start a new trial (M3). Also moves LastSeen forward.
    /// </summary>
    public LicenseRecord LoadOrStart(DateTime nowUtc)
    {
        LicenseRecord? r = null;
        try { if (File.Exists(Path)) r = JsonSerializer.Deserialize<LicenseRecord>(File.ReadAllText(Path), Json); }
        catch (Exception ex) when (ex is JsonException || ex is IOException) { r = null; }
        DateTime? marked = null;
        try { marked = _marker?.Read(); } catch { }   // a marker that can't be read must never stop the app
        if (marked == default(DateTime)) marked = null;
        DateTime? inFile = r != null && r.FirstRunUtc != default ? r.FirstRunUtc : null;
        var start = inFile is DateTime f && (marked is not DateTime m0 || f <= m0) ? f : marked ?? nowUtc;
        if (start > nowUtc.AddDays(1)) start = nowUtc;   // first run under a fast clock: don't count the trial as used up

        if (r == null || r.FirstRunUtc == default)
            r = new LicenseRecord { FirstRunUtc = start, LastSeenUtc = nowUtc };   // missing or unreadable file
        else
            r.FirstRunUtc = start;
        if (nowUtc > r.LastSeenUtc) r.LastSeenUtc = nowUtc;
        if (_marker != null && marked != start)
            try { _marker.Write(start); } catch { }
        Save(r);
        return r;
    }

    public void Save(LicenseRecord r)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
        File.Move(tmp, Path, overwrite: true);
    }
}
