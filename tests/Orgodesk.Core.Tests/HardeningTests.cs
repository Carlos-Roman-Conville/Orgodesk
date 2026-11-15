using System.Net;
using Orgodesk.Core.Licensing;
using Orgodesk.Core.Updates;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>M3: the trial start is also kept outside the data folder, and the earlier of the two wins.</summary>
public sealed class TrialMarkerTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SalesConfig Cfg = new("prod", "https://gum.co/x");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orgodesk-trialmarker-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class FakeMarker : ITrialMarker
    {
        public DateTime? Value;
        public bool Throws;
        public DateTime? Read() => Throws ? throw new UnauthorizedAccessException() : Value;
        public void Write(DateTime firstRunUtc) { if (Throws) throw new UnauthorizedAccessException(); Value = firstRunUtc; }
    }

    [Fact]
    public void FirstRun_WritesTheTrialStartToTheMarker()
    {
        var m = new FakeMarker();
        new LicenseStore(_root, m).LoadOrStart(T0);
        Assert.Equal(T0, m.Value);
    }

    [Fact]
    public void DeletingLicenseJson_DoesNotStartANewTrial()
    {
        var m = new FakeMarker();
        var store = new LicenseStore(_root, m);
        store.LoadOrStart(T0);
        File.Delete(store.Path);

        var r = store.LoadOrStart(T0.AddDays(20));

        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(LicenseState.Expired, LicenseRules.Evaluate(r, Cfg, T0.AddDays(20)).State);
    }

    [Fact]
    public void EditedFileWithLaterStart_UsesTheEarlierMarker()
    {
        var m = new FakeMarker { Value = T0 };
        var store = new LicenseStore(_root, m);
        store.Save(new LicenseRecord { FirstRunUtc = T0.AddDays(10), LastSeenUtc = T0.AddDays(10) });

        var r = store.LoadOrStart(T0.AddDays(15));

        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(T0, new LicenseStore(_root).LoadOrStart(T0.AddDays(15)).FirstRunUtc);   // and it is saved
    }

    [Fact]
    public void MarkerLaterThanFile_FileWins_AndMarkerIsMovedBack()
    {
        var m = new FakeMarker { Value = T0.AddDays(10) };
        var store = new LicenseStore(_root, m);
        store.Save(new LicenseRecord { FirstRunUtc = T0, LastSeenUtc = T0 });

        var r = store.LoadOrStart(T0.AddDays(11));

        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(T0, m.Value);
    }

    [Fact]
    public void ExistingUserWithNoMarker_GetsTheFileStartCopiedToTheMarker()
    {
        var store = new LicenseStore(_root);
        store.LoadOrStart(T0);
        var m = new FakeMarker();

        var r = new LicenseStore(_root, m).LoadOrStart(T0.AddDays(2));

        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(T0, m.Value);
    }

    [Fact]
    public void DeletingFile_KeepsKeyless_RecordFromMarkerOnly()
    {
        var m = new FakeMarker { Value = T0 };
        var r = new LicenseStore(_root, m).LoadOrStart(T0.AddDays(3));
        Assert.Equal(T0, r.FirstRunUtc);
        Assert.Equal(T0.AddDays(3), r.LastSeenUtc);
    }

    [Fact]
    public void BrokenMarker_NeverStopsTheApp()
    {
        var m = new FakeMarker { Throws = true };
        var r = new LicenseStore(_root, m).LoadOrStart(T0);
        Assert.Equal(T0, r.FirstRunUtc);
    }
}

/// <summary>M2: what the running app's daily / after-resume check sees on the same record.</summary>
public sealed class RunningLicenseCheckTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SalesConfig Cfg = new("prod", "https://gum.co/x");

    [Fact]
    public void PaidBuyer_NetworkErrorOnRecheck_KeepsRunningInsideGrace()
    {
        var r = new LicenseRecord { FirstRunUtc = T0, LastSeenUtc = T0, Key = "K", LastVerifiedUtc = T0 };
        var later = T0.AddDays(20);
        Assert.True(LicenseRules.RecheckDue(r, Cfg, later));

        GumroadVerifier.Apply(r, "K", new VerifyResult(VerifyOutcome.NetworkError, "offline"), later);

        Assert.True(LicenseRules.Evaluate(r, Cfg, later).CanRun);
    }

    [Fact]
    public void RefundSeenOnRecheck_StopsTheRunningCopy()
    {
        var r = new LicenseRecord { FirstRunUtc = T0, LastSeenUtc = T0, Key = "K", LastVerifiedUtc = T0 };
        var later = T0.AddDays(20);

        GumroadVerifier.Apply(r, "K", new VerifyResult(VerifyOutcome.Revoked, "refunded"), later);

        Assert.False(LicenseRules.Evaluate(r, Cfg, later).CanRun);
    }

    [Fact]
    public void TrialEndsWhileRunning_TheSameRecordTurnsExpired()
    {
        var r = new LicenseRecord { FirstRunUtc = T0, LastSeenUtc = T0 };
        Assert.True(LicenseRules.Evaluate(r, Cfg, T0.AddDays(13)).CanRun);
        Assert.False(LicenseRules.Evaluate(r, Cfg, T0.AddDays(14).AddHours(1)).CanRun);
    }
}

/// <summary>L1: the update link must point at the seller's own host.</summary>
public sealed class UpdateLinkHostTests
{
    private static readonly Version Current = new(1, 0, 0);
    private const string UpdateUrl = "https://updates.example.com/v.json";
    private const string BuyUrl = "https://seller.gumroad.com/l/orgodesk";
    private static string Json(string url) => "{\"version\":\"2.0.0\",\"url\":\"" + url + "\"}";

    [Theory]
    [InlineData("https://updates.example.com/download")]
    [InlineData("https://seller.gumroad.com/l/orgodesk")]
    [InlineData("https://UPDATES.example.com/download")]
    public void LinkOnTrustedHost_IsReported(string link) =>
        Assert.NotNull(UpdateChecker.Parse(Json(link), Current, UpdateUrl, BuyUrl));

    [Theory]
    [InlineData("https://evil.example.net/orgodesk.exe")]
    [InlineData("https://updates.example.com.evil.net/x")]
    [InlineData("https://updates.example.com@evil.net/x")]
    [InlineData("https://example.com/x")]
    public void LinkOnOtherHost_IsNoUpdate(string link) =>
        Assert.Null(UpdateChecker.Parse(Json(link), Current, UpdateUrl, BuyUrl));

    [Fact]
    public void EmptyBuyUrl_TrustsOnlyTheUpdateHost()
    {
        Assert.NotNull(UpdateChecker.Parse(Json("https://updates.example.com/d"), Current, UpdateUrl, ""));
        Assert.Null(UpdateChecker.Parse(Json("https://seller.gumroad.com/l/orgodesk"), Current, UpdateUrl, ""));
    }

    [Fact]
    public void Url_IsTheNormalisedAbsoluteUri()
    {
        var u = UpdateChecker.Parse(Json("https://updates.example.com/a b"), Current, UpdateUrl, BuyUrl);
        Assert.Equal("https://updates.example.com/a%20b", u!.Url);
    }

    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task Check_RejectsALinkOffTheUpdateAndBuyHosts()
    {
        var bad = await new UpdateChecker(UpdateUrl, new Stub(Json("https://evil.example.net/x.exe")), BuyUrl).CheckAsync(Current);
        Assert.Null(bad);
        var good = await new UpdateChecker(UpdateUrl, new Stub(Json("https://seller.gumroad.com/l/orgodesk")), BuyUrl).CheckAsync(Current);
        Assert.NotNull(good);
    }
}
