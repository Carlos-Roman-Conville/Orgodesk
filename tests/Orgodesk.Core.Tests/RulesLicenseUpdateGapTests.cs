using System.Drawing;
using System.Net;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Licensing;
using Orgodesk.Core.Rules;
using Orgodesk.Core.Updates;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>Licence rules and limits come from SALES.md: 14-day trial, 30 days offline, weekly recheck, 5 PCs per key.</summary>
public class LicenseBoundaryTests : IDisposable
{
    private static readonly SalesConfig Cfg = new("prod123", "https://example.gumroad.com/l/orgodesk");
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orgodesk-lic-gap", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static LicenseRecord Fresh() => new() { FirstRunUtc = T0, LastSeenUtc = T0 };
    private static LicenseRecord Keyed(DateTime verified) => new() { FirstRunUtc = T0.AddDays(-100), LastSeenUtc = verified, Key = "K", LastVerifiedUtc = verified };

    [Fact]
    public void Trial_OneTickBeforeDay14_IsStillTrial_WithOneDayLeft()
    {
        var s = LicenseRules.Evaluate(Fresh(), Cfg, T0.AddDays(14).AddTicks(-1));
        Assert.Equal(LicenseState.Trial, s.State);
        Assert.Equal(1, s.TrialDaysLeft);
        Assert.True(s.CanRun);
    }

    [Fact]
    public void Trial_ExactlyDay14_IsExpired_WithTheTrialEndedMessage()
    {
        var s = LicenseRules.Evaluate(Fresh(), Cfg, T0.AddDays(14));
        Assert.Equal(LicenseState.Expired, s.State);
        Assert.Contains("free trial has ended", s.Message);
    }

    [Fact]
    public void Trial_FirstMomentAfterStart_Shows14DaysLeft()
    {
        Assert.Equal(14, LicenseRules.Evaluate(Fresh(), Cfg, T0.AddTicks(1)).TrialDaysLeft);
    }

    [Fact]
    public void Trial_LastDayMessage_SaysOneDay_NotOneDays()
    {
        Assert.Equal("Trial: 1 day left", LicenseRules.Evaluate(Fresh(), Cfg, T0.AddDays(13)).Message);
    }

    [Fact]
    public void Trial_WithAKeyNeverVerified_StillRunsAsTrial_ThenExpiresWithTrialMessage()
    {
        var r = Fresh(); r.Key = "K";
        Assert.Equal(LicenseState.Trial, LicenseRules.Evaluate(r, Cfg, T0.AddDays(2)).State);
        Assert.Contains("free trial has ended", LicenseRules.Evaluate(r, Cfg, T0.AddDays(15)).Message);
    }

    [Fact]
    public void Offline_ExactlyThirtyDaysAfterTheLastCheck_IsStillLicensed()
    {
        var r = Keyed(T0);
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(30)).State);
    }

    [Fact]
    public void Offline_OneTickPastThirtyDays_IsNoLongerLicensed()
    {
        var r = Keyed(T0);
        var s = LicenseRules.Evaluate(r, Cfg, T0.AddDays(30).AddTicks(1));
        Assert.Equal(LicenseState.Expired, s.State);
        Assert.Contains("internet", s.Message);
    }

    [Fact]
    public void Offline_PastGrace_ButTrialStillRunning_FallsBackToTheTrial()
    {
        // A buyer whose key could not be checked for 31 days, on a copy that is itself only 5 days old.
        var r = new LicenseRecord { FirstRunUtc = T0.AddDays(26), LastSeenUtc = T0.AddDays(31), Key = "K", LastVerifiedUtc = T0 };
        Assert.Equal(LicenseState.Trial, LicenseRules.Evaluate(r, Cfg, T0.AddDays(31)).State);
    }

    [Fact]
    public void Recheck_ExactlySevenDays_IsDue_AndOneTickBeforeIsNot()
    {
        var r = Keyed(T0);
        Assert.False(LicenseRules.RecheckDue(r, Cfg, T0.AddDays(7).AddTicks(-1)));
        Assert.True(LicenseRules.RecheckDue(r, Cfg, T0.AddDays(7)));
    }

    [Fact]
    public void Recheck_NeverDue_WithoutAKey_ForADevBuild_OrForARevokedKey()
    {
        Assert.False(LicenseRules.RecheckDue(Fresh(), Cfg, T0.AddDays(99)));
        Assert.False(LicenseRules.RecheckDue(Keyed(T0), new SalesConfig("", ""), T0.AddDays(99)));
        var revoked = Keyed(T0); revoked.Revoked = true;
        Assert.False(LicenseRules.RecheckDue(revoked, Cfg, T0.AddDays(99)));
    }

    [Fact]
    public void Recheck_KeyThatWasNeverVerified_IsDueAtOnce()
    {
        var r = Fresh(); r.Key = "K";
        Assert.True(LicenseRules.RecheckDue(r, Cfg, T0));
    }

    [Fact]
    public void Recheck_ClockSetBack_DoesNotHideADueCheck()
    {
        var r = Keyed(T0); r.LastSeenUtc = T0.AddDays(10);
        Assert.True(LicenseRules.RecheckDue(r, Cfg, T0.AddDays(1)));
    }

    [Fact]
    public void RevokedKey_InsideTheGraceWindow_IsNotLicensed()
    {
        var r = Keyed(T0); r.Revoked = true;
        Assert.NotEqual(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(1)).State);
    }

    [Fact]
    public void RevokedKey_AfterTheTrial_SaysTheKeyIsNoLongerValid()
    {
        var r = Keyed(T0); r.Revoked = true;
        Assert.Contains("no longer valid", LicenseRules.Evaluate(r, Cfg, T0.AddDays(1)).Message);
    }

    [Fact]
    public void BlankKey_DoesNotLicense()
    {
        var r = Fresh(); r.Key = "   "; r.LastVerifiedUtc = T0;
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, T0.AddDays(20)).State);
    }

    [Fact]
    public void DevBuild_WhitespaceProductId_IsStillADevBuild()
    {
        Assert.Equal(LicenseState.DevBuild, LicenseRules.Evaluate(Fresh(), new SalesConfig("  ", ""), T0.AddDays(999)).State);
    }

    // ---- activation limit: 5 PCs per key ----

    private static readonly GumroadVerifier V = new(Cfg);
    private static string Body(int uses) => "{\"success\":true,\"uses\":" + uses + ",\"purchase\":{}}";

    [Fact]
    public void Interpret_FifthActivation_IsAccepted()
    {
        Assert.Equal(VerifyOutcome.Valid, V.Interpret(HttpStatusCode.OK, Body(5), activate: true).Outcome);
    }

    [Fact]
    public void Interpret_SixthActivation_IsRefused_WithTheLimitInTheMessage()
    {
        var res = V.Interpret(HttpStatusCode.OK, Body(6), activate: true);
        Assert.Equal(VerifyOutcome.TooManyActivations, res.Outcome);
        Assert.Contains("5 PCs", res.Message);
        Assert.Equal(6, res.Uses);
    }

    [Fact]
    public void Interpret_TooManyActivations_DoesNotChangeTheRecord()
    {
        var r = Fresh();
        GumroadVerifier.Apply(r, "K", V.Interpret(HttpStatusCode.OK, Body(9), true), T0);
        Assert.Null(r.Key);
        Assert.Null(r.LastVerifiedUtc);
        Assert.False(r.Revoked);
    }

    [Fact]
    public void Interpret_ARecheckOfAKeyAlreadyOverTheCount_StaysValid()
    {
        Assert.Equal(VerifyOutcome.Valid, V.Interpret(HttpStatusCode.OK, Body(8), activate: false).Outcome);
    }

    [Theory]
    [InlineData("{\"success\":true,\"purchase\":{\"chargebacked\":true}}")]
    [InlineData("{\"success\":true,\"purchase\":{\"disputed\":true}}")]
    public void Interpret_ChargebackOrDispute_RevokesTheKey(string body)
    {
        Assert.Equal(VerifyOutcome.Revoked, V.Interpret(HttpStatusCode.OK, body, false).Outcome);
    }

    [Fact]
    public void Interpret_ValidKeyWithNoUsesField_IsValid_WithNullCount()
    {
        var res = V.Interpret(HttpStatusCode.OK, "{\"success\":true}", true);
        Assert.Equal(VerifyOutcome.Valid, res.Outcome);
        Assert.Null(res.Uses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"success\":\"true\"}")]
    [InlineData("{\"success\":1}")]
    public void Interpret_UnexpectedAnswers_NeverActivateAKey(string body)
    {
        var res = V.Interpret(HttpStatusCode.OK, body, true);
        Assert.NotEqual(VerifyOutcome.Valid, res.Outcome);
        var r = Fresh();
        GumroadVerifier.Apply(r, "K", res, T0);
        Assert.Null(r.LastVerifiedUtc);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("5")]
    [InlineData("\"hello\"")]
    public void Interpret_NonObjectJsonAnswer_IsNotACrash(string body)
    {
        var res = V.Interpret(HttpStatusCode.OK, body, true);
        Assert.NotEqual(VerifyOutcome.Valid, res.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_BlankKey_IsRefusedWithoutCallingTheServer()
    {
        var h = new CountingHandler();
        var res = await new GumroadVerifier(Cfg, h).VerifyAsync("   ", activate: true);
        Assert.Equal(VerifyOutcome.Invalid, res.Outcome);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task VerifyAsync_NetworkDown_IsNetworkError_AndActivateAsyncStopsAfterOneCall()
    {
        var h = new CountingHandler { Throw = true };
        var res = await new GumroadVerifier(Cfg, h).ActivateAsync("KEY");
        Assert.Equal(VerifyOutcome.NetworkError, res.Outcome);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task VerifyAsync_KeyIsTrimmedBeforeSending()
    {
        var h = new CountingHandler { Reply = Body(1) };
        await new GumroadVerifier(Cfg, h).VerifyAsync("  ABC  ", activate: false);
        Assert.Contains("license_key=ABC&", h.LastBody + "&");
    }

    [Fact]
    public async Task ActivateAsync_UnknownKey_StopsAtThePeek()
    {
        var h = new CountingHandler { Status = HttpStatusCode.NotFound, Reply = "{\"success\":false}" };
        var res = await new GumroadVerifier(Cfg, h).ActivateAsync("NOPE");
        Assert.Equal(VerifyOutcome.Invalid, res.Outcome);
        Assert.Equal(1, h.Calls);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls; public bool Throw; public string Reply = "{\"success\":true,\"uses\":0,\"purchase\":{}}";
        public HttpStatusCode Status = HttpStatusCode.OK; public string LastBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Throw) throw new HttpRequestException("offline");
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }

    // ---- applying results ----

    [Fact]
    public void Apply_InvalidAnswerForADifferentKey_DoesNotRevokeTheStoredKey()
    {
        var r = Keyed(T0);
        GumroadVerifier.Apply(r, "TYPO", new VerifyResult(VerifyOutcome.Invalid, "x"), T0.AddDays(8));
        Assert.False(r.Revoked);
        Assert.Equal("K", r.Key);
    }

    [Fact]
    public void Apply_InvalidAnswerForTheStoredKey_Revokes()
    {
        var r = Keyed(T0);
        GumroadVerifier.Apply(r, " K ", new VerifyResult(VerifyOutcome.Invalid, "x"), T0.AddDays(8));
        Assert.True(r.Revoked);
    }

    [Fact]
    public void Apply_ValidAfterRevoked_RestoresTheLicense()
    {
        var r = Keyed(T0); r.Revoked = true;
        GumroadVerifier.Apply(r, "K", new VerifyResult(VerifyOutcome.Valid, "ok"), T0.AddDays(40));
        Assert.Equal(LicenseState.Licensed, LicenseRules.Evaluate(r, Cfg, T0.AddDays(40)).State);
    }

    [Fact]
    public void Apply_NetworkError_ChangesNothingAtAll()
    {
        var r = Keyed(T0);
        GumroadVerifier.Apply(r, "K", new VerifyResult(VerifyOutcome.NetworkError, "x"), T0.AddDays(8));
        Assert.Equal(T0, r.LastVerifiedUtc);
        Assert.Equal(T0, r.LastSeenUtc);
        Assert.False(r.Revoked);
    }

    // ---- license.json on disk ----

    [Fact]
    public void Store_KeyAndVerifiedTime_SurviveARestart()
    {
        var store = new LicenseStore(_root);
        var r = store.LoadOrStart(T0);
        GumroadVerifier.Apply(r, "KEY", new VerifyResult(VerifyOutcome.Valid, "ok"), T0.AddDays(1));
        store.Save(r);
        var back = new LicenseStore(_root).LoadOrStart(T0.AddDays(2));
        Assert.Equal("KEY", back.Key);
        Assert.Equal(T0, back.FirstRunUtc);
        Assert.Equal(T0.AddDays(1), back.LastVerifiedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ nope")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Store_DamagedOrEmptyFile_StartsAFreshTrial_WithoutThrowing(string content)
    {
        var store = new LicenseStore(_root);
        Directory.CreateDirectory(_root);
        File.WriteAllText(store.Path, content);
        var r = store.LoadOrStart(T0);
        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(LicenseState.Trial, LicenseRules.Evaluate(r, Cfg, T0).State);
    }

    [Fact]
    public void Store_ClockSetBack_KeepsTheTrialEnd()
    {
        var store = new LicenseStore(_root);
        store.LoadOrStart(T0);
        store.LoadOrStart(T0.AddDays(20));
        var r = store.LoadOrStart(T0.AddDays(1));         // user winds the clock back
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, T0.AddDays(1)).State);
    }

    [Fact]
    public void Store_Save_LeavesNoTempFile()
    {
        var store = new LicenseStore(_root);
        store.LoadOrStart(T0);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }
}

public class RulesGapTests
{
    private static ItemFacts File(string name, string? ext, bool shortcut = false) => new("k:" + name, name, ext, false, shortcut, false);
    private static Rule Ext(string pattern, ItemType t = ItemType.Any) => new() { Kind = RuleKind.Extension, Pattern = pattern, ItemType = t, TargetFenceId = "t" };
    private static Rule Name(string glob) => new() { Kind = RuleKind.NamePattern, Pattern = glob, TargetFenceId = "t" };

    [Theory]
    [InlineData("jpg;png", ".PNG", true)]
    [InlineData(".jpg, .png", ".jpg", true)]
    [InlineData("jpg png", "png", true)]
    [InlineData("jpg;;  ;png;", "png", true)]
    [InlineData("jpeg", ".jpg", false)]
    [InlineData("jp", ".jpg", false)]
    [InlineData("", ".jpg", false)]
    [InlineData("   ", ".jpg", false)]
    [InlineData("jpg", "", false)]
    [InlineData("jpg", null, false)]
    public void Extension_Patterns(string pattern, string? ext, bool expected)
    {
        Assert.Equal(expected, RulesEngine.Matches(Ext(pattern), File("x", ext)));
    }

    [Fact]
    public void Extension_NeverMatchesFoldersOrSystemItems()
    {
        var folder = new ItemFacts("k", "dir.txt", ".txt", true, false, false);
        var system = new ItemFacts("k", "Recycle Bin", ".txt", false, false, true);
        Assert.False(RulesEngine.Matches(Ext("txt"), folder));
        Assert.False(RulesEngine.Matches(Ext("txt"), system));
    }

    [Fact]
    public void Extension_WithItemTypeFilter_OnlyMatchesThatType()
    {
        var lnk = File("App", ".lnk", shortcut: true);
        Assert.True(RulesEngine.Matches(Ext("lnk", ItemType.Shortcut), lnk));
        Assert.False(RulesEngine.Matches(Ext("lnk", ItemType.File), lnk));
    }

    [Theory]
    [InlineData("Report*", "Report 2026.docx", true)]
    [InlineData("report*", "REPORT.txt", true)]
    [InlineData("*.log", "a.log", true)]
    [InlineData("*.log", "a.logx", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("a?c", "abbc", false)]
    [InlineData("v1.0 (final)", "v1.0 (final)", true)]
    [InlineData("v1.0", "v1x0", false)]
    [InlineData("[draft]*", "[draft] notes", true)]
    [InlineData("a+b", "aab", false)]
    [InlineData("a$", "a$", true)]
    [InlineData("^x", "^x", true)]
    [InlineData("", "anything", false)]
    [InlineData("*", "", true)]
    public void NamePattern_Glob(string glob, string name, bool expected)
    {
        Assert.Equal(expected, RulesEngine.Matches(Name(glob), File(name, null)));
    }

    [Fact]
    public void NamePattern_OnAVeryLongName_StillAnswers()
    {
        var name = new string('a', 100_000) + "!";
        Assert.False(RulesEngine.Matches(Name("*b"), File(name, null)));
        Assert.True(RulesEngine.Matches(Name("a*!"), File(name, null)));
    }

    [Fact]
    public void Match_NoRules_ReturnsNull()
    {
        Assert.Null(RulesEngine.Match(Array.Empty<Rule>(), File("a", ".txt")));
    }

    [Fact]
    public void Match_FirstMatchingRuleWins_EvenWhenALaterOneAlsoMatches()
    {
        var rules = new[]
        {
            new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "first" },
            new Rule { Kind = RuleKind.NamePattern, Pattern = "*", TargetFenceId = "second" },
        };
        Assert.Equal("first", RulesEngine.Match(rules, File("a.txt", ".txt")));
        Assert.Equal("second", RulesEngine.Match(rules, File("a.png", ".png")));
    }

    [Fact]
    public void ItemTypeRule_MatchesFolder_ButNotFile()
    {
        var rule = new Rule { Kind = RuleKind.ItemType, ItemType = ItemType.Folder, TargetFenceId = "t" };
        Assert.True(RulesEngine.Matches(rule, new ItemFacts("k", "d", null, true, false, false)));
        Assert.False(RulesEngine.Matches(rule, File("f", ".txt")));
    }

    [Fact]
    public void ItemType_ShortcutBeatsFolder_WhenBothFlagsAreSet()
    {
        Assert.Equal(ItemType.Shortcut, new ItemFacts("k", "n", null, true, true, false).Type);
        Assert.Equal(ItemType.System, new ItemFacts("k", "n", null, true, true, true).Type);
    }

    [Fact]
    public void Rule_Clone_IsIndependent()
    {
        var r = Ext("txt");
        var c = r.Clone();
        c.Pattern = "md";
        Assert.Equal("txt", r.Pattern);
    }
}

public class UpdateGapTests
{
    private static readonly Version Current = new(1, 0, 0);
    private static string Json(string version, string extra = "") => "{\"version\":\"" + version + "\",\"url\":\"https://example.com/get\"" + extra + "}";

    [Theory]
    [InlineData("1.0.1")]
    [InlineData("1.1")]
    [InlineData("1.0.10")]
    [InlineData("1.10.0")]
    public void Newer_IsReported(string v) => Assert.NotNull(UpdateChecker.Parse(Json(v), Current));

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0")]
    [InlineData("0.99.99")]
    [InlineData("")]
    [InlineData("1.x")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("99999999999.0.0")]
    public void NotNewerOrNotAVersion_IsNoUpdate(string v) => Assert.Null(UpdateChecker.Parse(Json(v), Current));

    [Fact]
    public void RunningVersionWithFourParts_IsComparedByMajorMinorBuild()
    {
        Assert.Null(UpdateChecker.Parse(Json("1.0.5"), new Version(1, 0, 5, 7)));
        Assert.NotNull(UpdateChecker.Parse(Json("1.0.6"), new Version(1, 0, 5, 7)));
    }

    [Fact]
    public void RunningVersionWithTwoParts_MatchesTheSameThreePartRelease()
    {
        Assert.Null(UpdateChecker.Parse(Json("1.2.0"), new Version(1, 2)));
        Assert.NotNull(UpdateChecker.Parse(Json("1.2.1"), new Version(1, 2)));
    }

    [Fact]
    public void LongNotes_AreCutAt200Characters()
    {
        var u = UpdateChecker.Parse(Json("2.0.0", ",\"notes\":\"" + new string('n', 5000) + "\""), Current);
        Assert.Equal(200, u!.Notes.Length);
    }

    [Theory]
    [InlineData(",\"notes\":42")]
    [InlineData(",\"notes\":null")]
    [InlineData(",\"notes\":[\"a\"]")]
    [InlineData("")]
    public void MissingOrWrongTypedNotes_BecomeEmpty_AndStillAnnounceTheUpdate(string extra)
    {
        var u = UpdateChecker.Parse(Json("2.0.0", extra), Current);
        Assert.NotNull(u);
        Assert.Equal("", u!.Notes);
    }

    [Theory]
    [InlineData("{\"version\":2,\"url\":\"https://example.com\"}")]
    [InlineData("{\"version\":null,\"url\":\"https://example.com\"}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":null}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":5}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"https://\"}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"\"}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"javascript:alert(1)\"}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"//example.com/x\"}")]
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"ftp://example.com/x\"}")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    public void Malformed_IsNoUpdate_AndNeverThrows(string json) => Assert.Null(UpdateChecker.Parse(json, Current));

    [Fact]
    public async Task Check_ServerError_IsNoUpdate()
    {
        var u = await new UpdateChecker("https://example.com/v.json", new Stub(HttpStatusCode.InternalServerError, "oops")).CheckAsync(Current);
        Assert.Null(u);
    }

    [Fact]
    public async Task Check_Redirect_Answer_IsNoUpdate()
    {
        var u = await new UpdateChecker("https://example.com/v.json", new Stub(HttpStatusCode.Found, "")).CheckAsync(Current);
        Assert.Null(u);
    }

    [Fact]
    public async Task Check_TimeoutOrCancel_IsNoUpdate()
    {
        var u = await new UpdateChecker("https://example.com/v.json", new Stub(HttpStatusCode.OK, "", throwTimeout: true)).CheckAsync(Current);
        Assert.Null(u);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://example.com/v.json")]
    public async Task Check_BadAddress_IsNoUpdate_AndNothingIsSent(string url)
    {
        var stub = new Stub(HttpStatusCode.OK, Json("2.0.0"));
        Assert.Null(await new UpdateChecker(url, stub).CheckAsync(Current));
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Check_FileJustUnderTheSizeLimit_IsRead()
    {
        var pad = new string(' ', 16 * 1024 - Json("2.0.0").Length);
        Assert.NotNull(await new UpdateChecker("https://example.com/v.json", new Stub(HttpStatusCode.OK, Json("2.0.0") + pad)).CheckAsync(Current));
    }

    [Fact]
    public async Task Check_FileOneCharOverTheSizeLimit_IsIgnored()
    {
        var pad = new string(' ', 16 * 1024 - Json("2.0.0").Length + 1);
        Assert.Null(await new UpdateChecker("https://example.com/v.json", new Stub(HttpStatusCode.OK, Json("2.0.0") + pad)).CheckAsync(Current));
    }

    private sealed class Stub(HttpStatusCode status, string body, bool throwTimeout = false) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (throwTimeout) throw new TaskCanceledException("timeout");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

public class PresetsGapTests
{
    private static Layout Desk()
    {
        var l = new Layout();
        l.ActivePage.Fences.Add(new Fence { Id = "dev", Title = "Dev", Bounds = new Rectangle(0, 0, 300, 200), Items = { "a", "b" } });
        l.ActivePage.FreeItems["c"] = new Point(400, 0);
        return l;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_BlankName_IsRefused_AndChangesNothing(string name)
    {
        var l = Desk();
        Assert.Throws<ArgumentException>(() => Presets.Save(l, name));
        Assert.Empty(l.Presets);
        Assert.Null(l.ActivePresetId);
    }

    [Fact]
    public void Save_TrimsTheName_AndFindIgnoresCase()
    {
        var l = Desk();
        var p = Presets.Save(l, "  Work  ");
        Assert.Equal("Work", p.Name);
        Assert.Same(p, Presets.Find(l, "WORK"));
    }

    [Fact]
    public void Save_SameNameInOtherCase_ReplacesAndKeepsTheId()
    {
        var l = Desk();
        var first = Presets.Save(l, "Work");
        var id = first.Id;
        l.ActivePage.FreeItems["d"] = new Point(0, 500);
        var second = Presets.Save(l, "work");
        Assert.Single(l.Presets);
        Assert.Equal(id, second.Id);
        Assert.Contains("d", second.FreeItems.Keys);
    }

    [Fact]
    public void Save_PresetDoesNotShareTheActivePagesId()
    {
        var l = Desk();
        Assert.NotEqual(l.ActivePage.Id, Presets.Save(l, "W").Id);
    }

    [Fact]
    public void Apply_UnknownId_ChangesNothing()
    {
        var l = Desk();
        Assert.False(Presets.Apply(l, "nope"));
        Assert.Equal(2, l.ActivePage.FindFence("dev")!.Items.Count);
        Assert.Null(l.ActivePresetId);
    }

    [Fact]
    public void Apply_KeepsThePagesOwnNameAndId()
    {
        var l = Desk();
        l.ActivePage.Name = "Mine";
        var pageId = l.ActivePage.Id;
        var p = Presets.Save(l, "Work");
        Presets.Apply(l, p.Id);
        Assert.Equal("Mine", l.ActivePage.Name);
        Assert.Equal(pageId, l.ActivePage.Id);
    }

    [Fact]
    public void Apply_OnlyTouchesTheActivePage()
    {
        var l = Desk();
        var other = new Page { Name = "Other" };
        other.FreeItems["z"] = new Point(0, 0);
        l.Pages.Add(other);
        var p = Presets.Save(l, "Work");
        Presets.Apply(l, p.Id);
        Assert.Contains("z", other.FreeItems.Keys);
    }

    [Fact]
    public void Update_UnknownId_ReturnsFalse_AndKeepsTheActivePreset()
    {
        var l = Desk();
        var p = Presets.Save(l, "Work");
        Assert.False(Presets.Update(l, "nope"));
        Assert.Equal(p.Id, l.ActivePresetId);
    }

    [Fact]
    public void Delete_AnotherPreset_KeepsTheActiveOne()
    {
        var l = Desk();
        var a = Presets.Save(l, "A");
        var b = Presets.Save(l, "B");          // B is active
        Assert.True(Presets.Delete(l, a.Id));
        Assert.Equal(b.Id, l.ActivePresetId);
        Assert.Same(b, Presets.Active(l));
    }

    [Fact]
    public void Delete_UnknownId_ReturnsFalse()
    {
        var l = Desk();
        Presets.Save(l, "A");
        Assert.False(Presets.Delete(l, "nope"));
        Assert.Single(l.Presets);
    }

    [Fact]
    public void Active_WhenTheIdPointsAtNothing_IsNull()
    {
        var l = Desk();
        l.ActivePresetId = "ghost";
        Assert.Null(Presets.Active(l));
    }
}
