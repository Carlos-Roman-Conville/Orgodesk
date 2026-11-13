using System.Text.Json;

namespace Orgodesk.Core.Updates;

/// <summary>A newer version announced by the update file.</summary>
public sealed record UpdateInfo(Version Version, string Url, string Notes);

/// <summary>
/// Reads a small JSON file the seller hosts, e.g. {"version":"1.0.1","url":"https://...","notes":"..."},
/// and says whether it names a version newer than the running one. It only reads: nothing is downloaded or
/// installed, and nothing about the PC is sent. Any failure (offline, bad file) means "no update".
/// </summary>
public sealed class UpdateChecker
{
    private const int MaxBytes = 16 * 1024;
    private readonly HttpClient _http;
    private readonly string _url;

    public UpdateChecker(string url, HttpMessageHandler? handler = null)
    {
        _url = url;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        if (!IsHttps(_url)) return null;
        try
        {
            using var resp = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentLength is > MaxBytes) return null;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return text.Length > MaxBytes ? null : Parse(text, current);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The update the file announces, or null if it is malformed, not newer, or its link is not https.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return null;
            if (!Version.TryParse(v.GetString(), out var version)) return null;
            if (!root.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String) return null;
            var url = u.GetString() ?? "";
            if (!IsHttps(url)) return null;
            var notes = root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            if (notes.Length > 200) notes = notes[..200];
            return Normalize(version) > Normalize(current) ? new UpdateInfo(version, url, notes) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // "1.0" and "1.0.0" are the same release; Version treats missing parts as -1.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
