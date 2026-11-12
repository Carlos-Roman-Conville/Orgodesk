using System.Net;
using System.Text.Json;

namespace Orgodesk.Core.Licensing;

public enum VerifyOutcome { Valid, Invalid, Revoked, TooManyActivations, NetworkError }

public sealed record VerifyResult(VerifyOutcome Outcome, string Message, int? Uses = null);

/// <summary>
/// Checks a key with Gumroad's license API (POST https://api.gumroad.com/v2/licenses/verify).
/// Gumroad answers 200 with success=true for a known key and 404 with success=false for an unknown one.
/// </summary>
public sealed class GumroadVerifier
{
    public const string Endpoint = "https://api.gumroad.com/v2/licenses/verify";
    private readonly HttpClient _http;
    private readonly SalesConfig _cfg;

    public GumroadVerifier(SalesConfig cfg, HttpMessageHandler? handler = null)
    {
        _cfg = cfg;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <param name="activate">True on first entry of a key: counts one use, so the activation limit applies.</param>
    public async Task<VerifyResult> VerifyAsync(string key, bool activate, CancellationToken ct = default)
    {
        key = key.Trim();
        if (key.Length == 0) return new VerifyResult(VerifyOutcome.Invalid, "Enter a license key.");
        HttpResponseMessage resp;
        string body;
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["product_id"] = _cfg.ProductId,
                ["license_key"] = key,
                ["increment_uses_count"] = activate ? "true" : "false",
            });
            resp = await _http.PostAsync(Endpoint, form, ct).ConfigureAwait(false);
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            return new VerifyResult(VerifyOutcome.NetworkError, "Could not reach the license server. Check your internet connection and try again.");
        }
        return Interpret(resp.StatusCode, body, activate);
    }

    /// <summary>
    /// First activation of a key on a PC (FC-41). Looks at the count first without changing it, and only counts the
    /// activation when there is room. A refused attempt must not raise the count, or every retry would push the buyer
    /// further over the limit and a support reset would be undone by the next try.
    /// </summary>
    public async Task<VerifyResult> ActivateAsync(string key, CancellationToken ct = default)
    {
        var peek = await VerifyAsync(key, activate: false, ct).ConfigureAwait(false);
        if (peek.Outcome != VerifyOutcome.Valid) return peek;
        if (peek.Uses is int uses && uses >= _cfg.MaxActivations)
            return new VerifyResult(VerifyOutcome.TooManyActivations,
                $"This key has already been activated {uses} times; the limit is {_cfg.MaxActivations} PCs. Contact support to reset it.", uses);
        return await VerifyAsync(key, activate: true, ct).ConfigureAwait(false);
    }

    public VerifyResult Interpret(HttpStatusCode status, string body, bool activate)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement; }
        catch (JsonException)
        {
            return (int)status >= 500
                ? new VerifyResult(VerifyOutcome.NetworkError, "The license server had a problem. Try again in a few minutes.")
                : new VerifyResult(VerifyOutcome.NetworkError, "Unexpected answer from the license server.");
        }
        bool success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        if (!success)
        {
            if ((int)status >= 500) return new VerifyResult(VerifyOutcome.NetworkError, "The license server had a problem. Try again in a few minutes.");
            return new VerifyResult(VerifyOutcome.Invalid, "That license key was not found. Check it against your Gumroad receipt.");
        }
        if (root.TryGetProperty("purchase", out var p))
        {
            if (Flag(p, "refunded") || Flag(p, "chargebacked") || Flag(p, "disputed"))
                return new VerifyResult(VerifyOutcome.Revoked, "This purchase was refunded or disputed, so the key no longer works.");
        }
        int? count = root.TryGetProperty("uses", out var u) && u.TryGetInt32(out int n) ? n : null;
        if (activate && count is int uses && uses > _cfg.MaxActivations)
            return new VerifyResult(VerifyOutcome.TooManyActivations,
                $"This key has been activated {uses} times; the limit is {_cfg.MaxActivations} PCs. Contact support to reset it.", uses);
        return new VerifyResult(VerifyOutcome.Valid, "License activated. Thank you for buying Orgodesk!", count);
    }

    private static bool Flag(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Applies a verify result to the record. A network error changes nothing (the offline grace covers it).</summary>
    public static void Apply(LicenseRecord r, string key, VerifyResult res, DateTime nowUtc)
    {
        switch (res.Outcome)
        {
            case VerifyOutcome.Valid:
                r.Key = key.Trim();
                r.LastVerifiedUtc = nowUtc;
                r.Revoked = false;
                // Gumroad just confirmed the key, so the clock-rollback guard isn't needed: reset it. Otherwise a PC whose
                // clock was once set to a future date would read a fresh check as weeks old and lock out a paying buyer.
                r.LastSeenUtc = nowUtc;
                break;
            case VerifyOutcome.Revoked:
                r.Key = key.Trim();
                r.Revoked = true;
                break;
            case VerifyOutcome.Invalid:
                // A recheck of a stored key that Gumroad no longer knows: revoke it.
                if (r.Key == key.Trim()) r.Revoked = true;
                break;
        }
    }
}
