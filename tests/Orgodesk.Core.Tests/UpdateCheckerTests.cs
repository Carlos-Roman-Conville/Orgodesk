using System.Net;
using Orgodesk.Core.Updates;
using Xunit;

namespace Orgodesk.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 0, 0);

    [Fact]
    public void NewerVersion_IsReported()
    {
        var u = UpdateChecker.Parse("{\"version\":\"1.0.1\",\"url\":\"https://example.com/get\",\"notes\":\"Fixes\"}", Current);
        Assert.NotNull(u);
        Assert.Equal(new Version(1, 0, 1), u!.Version);
        Assert.Equal("https://example.com/get", u.Url);
        Assert.Equal("Fixes", u.Notes);
    }

    [Theory]
    [InlineData("{\"version\":\"1.0.0\",\"url\":\"https://example.com\"}")]   // same
    [InlineData("{\"version\":\"1.0\",\"url\":\"https://example.com\"}")]     // same, short form
    [InlineData("{\"version\":\"0.9.9\",\"url\":\"https://example.com\"}")]   // older
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"http://example.com\"}")]    // link not https
    [InlineData("{\"version\":\"2.0.0\",\"url\":\"file:///C:/x.exe\"}")]      // link not a web page
    [InlineData("{\"version\":\"2.0.0\"}")]                                   // no link
    [InlineData("{\"version\":\"soon\",\"url\":\"https://example.com\"}")]    // not a version
    [InlineData("[1,2,3]")]
    [InlineData("<html>Not found</html>")]
    [InlineData("")]
    public void AnythingElse_IsNoUpdate(string json) => Assert.Null(UpdateChecker.Parse(json, Current));

    private sealed class Fake(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network");
    }

    [Fact]
    public async Task Check_ReadsTheFile()
    {
        var fake = new Fake(HttpStatusCode.OK, "{\"version\":\"1.2.0\",\"url\":\"https://example.com/get\"}");
        var u = await new UpdateChecker("https://example.com/version.json", fake).CheckAsync(Current);
        Assert.Equal(new Version(1, 2, 0), u!.Version);
    }

    [Fact]
    public async Task Check_Offline_Or_NotFound_IsNoUpdate()
    {
        Assert.Null(await new UpdateChecker("https://example.com/version.json", new Offline()).CheckAsync(Current));
        Assert.Null(await new UpdateChecker("https://example.com/version.json", new Fake(HttpStatusCode.NotFound, "x")).CheckAsync(Current));
    }

    [Fact]
    public async Task Check_HugeFile_IsIgnored()
    {
        var big = "{\"version\":\"9.0.0\",\"url\":\"https://example.com\",\"notes\":\"" + new string('x', 20000) + "\"}";
        Assert.Null(await new UpdateChecker("https://example.com/version.json", new Fake(HttpStatusCode.OK, big)).CheckAsync(Current));
    }

    [Fact]
    public async Task Check_NonHttpsAddress_IsNeverCalled()
    {
        var fake = new Fake(HttpStatusCode.OK, "{\"version\":\"9.0.0\",\"url\":\"https://example.com\"}");
        Assert.Null(await new UpdateChecker("http://example.com/version.json", fake).CheckAsync(Current));
        Assert.Null(await new UpdateChecker("", fake).CheckAsync(Current));
        Assert.Equal(0, fake.Calls);
    }
}
