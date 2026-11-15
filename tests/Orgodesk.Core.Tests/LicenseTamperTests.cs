using System.Net;
using Orgodesk.Core.Licensing;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>Security audit 2026-11-15, findings H1 (future dates in license.json) and M1 (non-404 refusals revoking a key).</summary>
public class LicenseTamperTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SalesConfig Cfg = new("prod123", "https://example.gumroad.com/l/orgodesk");

    [Fact]
    public void FutureLastVerified_IsNotLicensed_AndForcesRecheck()
    {
        var far = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var r = new LicenseRecord { Key = "x", FirstRunUtc = Now.AddDays(-60), LastSeenUtc = far, LastVerifiedUtc = far };
        Assert.NotEqual(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, Now).State);
        Assert.True(LicenseRules.RecheckDue(r, Cfg, Now));
    }

    [Fact]
    public void FutureFirstRun_DoesNotGiveEndlessTrial()
    {
        var far = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var r = new LicenseRecord { FirstRunUtc = far, LastSeenUtc = far };
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, Now).State);
    }

    [Fact]
    public void CheckSavedUnderFastClock_StaysLicensedOffline()
    {
        // Review C1: the clock ran 5 days fast at the last good check, then Windows corrected it; now offline.
        var fast = Now.AddDays(5);
        var r = new LicenseRecord { Key = "KEY", FirstRunUtc = Now.AddDays(-60), LastSeenUtc = fast, LastVerifiedUtc = fast };
        GumroadVerifier.Apply(r, "KEY", new VerifyResult(VerifyOutcome.NetworkError, "offline"), Now);
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, Now).State);
        Assert.True(LicenseRules.RecheckDue(r, Cfg, Now));   // the next online check repairs the dates
    }

    [Fact]
    public void FirstRunUnderFastClock_TrialStartsToday()
    {
        // Review m1: license.json written while the clock was 10 days fast; next start with the right clock.
        var dir = Path.Combine(Path.GetTempPath(), "orgodesk-tamper-" + Guid.NewGuid().ToString("N"));
        try
        {
            new LicenseStore(dir).LoadOrStart(Now.AddDays(10));
            var r = new LicenseStore(dir).LoadOrStart(Now);
            Assert.Equal(LicenseState.Trial, LicenseRules.Evaluate(r, Cfg, Now).State);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RecentVerify_StillLicensed()
    {
        var r = new LicenseRecord { Key = "KEY", FirstRunUtc = Now.AddDays(-60), LastSeenUtc = Now, LastVerifiedUtc = Now.AddDays(-3) };
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, Now).State);
        Assert.False(LicenseRules.RecheckDue(r, Cfg, Now));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void NonNotFoundRefusal_DoesNotRevoke(HttpStatusCode status)
    {
        var res = new GumroadVerifier(Cfg).Interpret(status, "{\"success\":false,\"message\":\"x\"}", activate: false);
        Assert.Equal(VerifyOutcome.NetworkError, res.Outcome);
        var r = new LicenseRecord { Key = "KEY", FirstRunUtc = Now.AddDays(-60), LastSeenUtc = Now, LastVerifiedUtc = Now.AddDays(-8) };
        GumroadVerifier.Apply(r, "KEY", res, Now);
        Assert.False(r.Revoked);
    }

    [Fact]
    public void NotFound_StillRevokesStoredKey()
    {
        var res = new GumroadVerifier(Cfg).Interpret(HttpStatusCode.NotFound, "{\"success\":false,\"message\":\"That license does not exist for the provided product.\"}", activate: false);
        Assert.Equal(VerifyOutcome.Invalid, res.Outcome);
    }
}
