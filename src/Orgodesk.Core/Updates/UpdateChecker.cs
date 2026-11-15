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
    private readonly string _buyUrl;

    /// <param name="buyUrl">The store page. An update link may point at its host or the update file's host, nowhere else.</param>
    public UpdateChecker(string url, HttpMessageHandler? handler = null, string? buyUrl = null)
    {
        _url = url;
        _buyUrl = buyUrl ?? "";
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
            return text.Length > MaxBytes ? null : Parse(text, current, _url, _buyUrl);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The update the file announces, or null if it is malformed, not newer, or its link is not https. When
    /// <paramref name="trustedUrls"/> are given (CheckAsync always gives the update and buy addresses), the link's host
    /// must also equal one of their hosts (L1). With none given only the https rule applies.
    /// </summary>
    public static UpdateInfo? Parse(string json, Version current, params string[] trustedUrls)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return null;
            if (!Version.TryParse(v.GetString(), out var version)) return null;
            if (!root.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String) return null;
            if (!Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            if (trustedUrls.Length > 0 && !trustedUrls.Any(t => Uri.TryCreate(t, UriKind.Absolute, out var tu)
                    && tu.Scheme == Uri.UriSchemeHttps && string.Equals(tu.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase)))
                return null;
            var notes = root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            if (notes.Length > 200) notes = notes[..200];
            return Normalize(version) > Normalize(current) ? new UpdateInfo(version, uri.AbsoluteUri, notes) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // "1.0" and "1.0.0" are the same release; Version treats missing parts as -1.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
