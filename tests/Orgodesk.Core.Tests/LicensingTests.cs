using System.Net;
using Orgodesk.Core.Licensing;
using Xunit;

namespace Orgodesk.Core.Tests;

public class LicensingTests : IDisposable
{
    private static readonly SalesConfig Cfg = new("prod123", "https://example.gumroad.com/l/orgodesk");
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orgodesk-lic-tests", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static LicenseRecord Fresh() => new() { FirstRunUtc = T0, LastSeenUtc = T0 };

    [Fact]
    public void DevBuild_AlwaysRuns()
    {
        var s = LicenseRules.Evaluate(Fresh(), new SalesConfig("", ""), T0.AddDays(400));
        Assert.Equal(LicenseState.DevBuild, s.State);
        Assert.True(s.CanRun);
    }

    [Fact]
    public void Trial_CountsDown_ThenExpires()
    {
        var r = Fresh();
        Assert.Equal(14, LicenseRules.Evaluate(r, Cfg, T0).TrialDaysLeft);
        Assert.Equal(1, LicenseRules.Evaluate(r, Cfg, T0.AddDays(13.5)).TrialDaysLeft);
        var end = LicenseRules.Evaluate(r, Cfg, T0.AddDays(14));
        Assert.Equal(LicenseState.Expired, end.State);
        Assert.False(end.CanRun);
    }

    [Fact]
    public void ClockSetBack_DoesNotExtendTrial()
    {
        var r = Fresh();
        r.LastSeenUtc = T0.AddDays(20);
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, T0.AddDays(1)).State);
    }

    [Fact]
    public void Licensed_HoldsThroughOfflineGrace_ThenNeedsRecheck()
    {
        var r = Fresh();
        r.Key = "K"; r.LastVerifiedUtc = T0.AddDays(20);
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(49)).State);
        var late = LicenseRules.Evaluate(r, Cfg, T0.AddDays(51));
        Assert.Equal(LicenseState.Expired, late.State);
        Assert.Contains("internet", late.Message);
    }

    [Fact]
    public void Revoked_KeyStopsCounting()
    {
        var r = Fresh();
        r.Key = "K"; r.LastVerifiedUtc = T0.AddDays(20); r.Revoked = true;
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, T0.AddDays(21)).State);
    }

    [Fact]
    public void RecheckDue_AfterAWeek()
    {
        var r = Fresh();
        r.Key = "K"; r.LastVerifiedUtc = T0;
        Assert.False(LicenseRules.RecheckDue(r, Cfg, T0.AddDays(6)));
        Assert.True(LicenseRules.RecheckDue(r, Cfg, T0.AddDays(7)));
    }

    [Fact]
    public void Store_StartsTrialOnce_AndKeepsIt()
    {
        var store = new LicenseStore(_root);
        var a = store.LoadOrStart(T0);
        var b = store.LoadOrStart(T0.AddDays(3));
        Assert.Equal(T0, b.FirstRunUtc);
        Assert.Equal(T0.AddDays(3), b.LastSeenUtc);
        var c = store.LoadOrStart(T0.AddDays(1)); // clock set back: LastSeen does not move back
        Assert.Equal(T0.AddDays(3), c.LastSeenUtc);
    }

    private static readonly GumroadVerifier V = new(Cfg);

    [Fact]
    public void Gumroad_ValidKey()
    {
        var res = V.Interpret(HttpStatusCode.OK, """{"success":true,"uses":1,"purchase":{"refunded":false,"chargebacked":false}}""", true);
        Assert.Equal(VerifyOutcome.Valid, res.Outcome);
        var r = Fresh();
        GumroadVerifier.Apply(r, " KEY-1 ", res, T0);
        Assert.Equal("KEY-1", r.Key);
        Assert.Equal(T0, r.LastVerifiedUtc);
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(100 - 75)).State);
    }

    [Fact]
    public void Gumroad_UnknownKey()
    {
        var res = V.Interpret(HttpStatusCode.NotFound, """{"success":false,"message":"That license does not exist for the provided product."}""", true);
        Assert.Equal(VerifyOutcome.Invalid, res.Outcome);
    }

    [Fact]
    public void Gumroad_Refunded()
    {
        var res = V.Interpret(HttpStatusCode.OK, """{"success":true,"uses":1,"purchase":{"refunded":true}}""", false);
        Assert.Equal(VerifyOutcome.Revoked, res.Outcome);
        var r = Fresh(); r.Key = "K"; r.LastVerifiedUtc = T0;
        GumroadVerifier.Apply(r, "K", res, T0.AddDays(8));
        Assert.True(r.Revoked);
    }

    [Fact]
    public void Gumroad_TooManyActivations_OnlyOnActivate()
    {
        var body = """{"success":true,"uses":6,"purchase":{}}""";
        Assert.Equal(VerifyOutcome.TooManyActivations, V.Interpret(HttpStatusCode.OK, body, true).Outcome);
        Assert.Equal(VerifyOutcome.Valid, V.Interpret(HttpStatusCode.OK, body, false).Outcome);
    }

    [Fact]
    public void Gumroad_ServerError_IsNetworkError_AndChangesNothing()
    {
        var res = V.Interpret(HttpStatusCode.BadGateway, "<html>bad gateway</html>", false);
        Assert.Equal(VerifyOutcome.NetworkError, res.Outcome);
        var r = Fresh(); r.Key = "K"; r.LastVerifiedUtc = T0;
        GumroadVerifier.Apply(r, "K", res, T0.AddDays(8));
        Assert.False(r.Revoked);
        Assert.Equal(T0, r.LastVerifiedUtc);
    }

    [Fact]
    public async Task Verify_SendsProductIdAndKey()
    {
        var handler = new CaptureHandler("""{"success":true,"uses":1,"purchase":{}}""");
        var res = await new GumroadVerifier(Cfg, handler).VerifyAsync("ABC", activate: true);
        Assert.Equal(VerifyOutcome.Valid, res.Outcome);
        Assert.Equal(GumroadVerifier.Endpoint, handler.Url);
        Assert.Contains("product_id=prod123", handler.Body);
        Assert.Contains("license_key=ABC", handler.Body);
        Assert.Contains("increment_uses_count=true", handler.Body);
    }

    private sealed class CaptureHandler(string reply) : HttpMessageHandler
    {
        public string Url = "", Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Url = req.RequestUri!.ToString();
            Body = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }

    [Fact]
    public void ClockOnceInTheFuture_DoesNotLockOutAPayingBuyer()
    {
        // The PC clock was set a year ahead once (LastSeen pinned there), then corrected. The buyer's key checks out.
        var r = Fresh();
        r.LastSeenUtc = T0.AddDays(365);
        var res = new GumroadVerifier(Cfg).Interpret(HttpStatusCode.OK, "{\"success\":true,\"uses\":1,\"purchase\":{}}", activate: true);
        GumroadVerifier.Apply(r, "KEY", res, T0.AddDays(2));
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(2)).State);
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(20)).State);
    }
}
