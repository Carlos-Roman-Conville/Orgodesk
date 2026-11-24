using System.Text.Json.Nodes;
using Crc.AppShell.Commands;
using Crc.AppShell.Licensing;
using Crc.AppShell.Pro;
using Orgodesk.App;

namespace Orgodesk.Commands.Tests;

/// <summary>
/// The AI-connection gate (crc-appshell kit-1.10, D-3 to D-9 and D-11, bundle-only): Orgodesk's command mode answers only
/// what the suite bundle's licence allows. Licences are built from records in a scratch folder: never the registry,
/// never Gumroad, never the real data folder.
/// </summary>
public class CommandGateTests : IDisposable
{
    private readonly Desk _d = new();
    public void Dispose() => _d.Dispose();

    private const string Status = """{"protocol":1,"verb":"status"}""";
    private const string List = """{"protocol":1,"verb":"list","action":"groups"}""";
    private const string Preview = """{"protocol":1,"verb":"preview","action":"new_group","args":{"name":"School"}}""";

    private static readonly CommandSales Release = new("Orgodesk", DevBuild: false, BundleProductId: "bundle-test", BundleBuyUrl: "https://example.com/bundle");

    private CommandLicense Make(string state, bool readOnly = false)
    {
        var sales = Release with { ReadOnlyAfterTrial = readOnly };
        var now = DateTime.UtcNow;
        var bundle = state switch
        {
            "trial" => new LicenseRecord { FirstRunUtc = now.AddDays(-5), LastSeenUtc = now },
            "bundle" => new LicenseRecord { FirstRunUtc = now.AddDays(-60), LastSeenUtc = now, Key = "BUNDLE-KEY", LastVerifiedUtc = now.AddDays(-1) },
            _ => new LicenseRecord { FirstRunUtc = now.AddDays(-60), LastSeenUtc = now },
        };
        return new CommandLicense(sales, new ProLicense(sales.Bundle, new LicenseStore(Path.Combine(_d.Root, "licence", "bundle")), bundle, null));
    }

    private (int Code, JsonNode Answer) Serve(CommandLicense license, string request)
    {
        var output = new StringWriter();
        int code = Program.CommandMode(_d.Data, _d.Lock, license, new StringReader(request), output);
        return (code, JsonNode.Parse(output.ToString())!);
    }

    [Theory]
    [InlineData("trial", 25)]
    [InlineData("bundle", 0)]
    public void With_the_suite_trial_or_the_bundle_key_every_verb_runs_and_status_reports_the_connection(string state, int daysAtLeast)
    {
        var (code, answer) = Serve(Make(state), Status);
        Assert.Equal(0, code);
        Assert.Equal("orgodesk", answer["tool"]!.GetValue<string>());
        var ai = answer["result"]!["aiConnection"]!;
        Assert.Equal("full", ai["access"]!.GetValue<string>());
        Assert.True(ai["trialDaysLeft"]!.GetValue<int>() >= daysAtLeast);
        var preview = Serve(Make(state), Preview).Answer;
        Assert.True(preview["ok"]!.GetValue<bool>(), preview.ToJsonString());   // reaches the tool
    }

    [Fact]
    public void After_the_trial_without_the_bundle_everything_is_refused_and_nothing_changes()
    {
        var before = _d.Files();
        var (code, answer) = Serve(Make("over"), Status);
        Assert.Equal(1, code);
        Assert.Equal("needs_license", Desk.Error(answer));
        Assert.Equal("needs_license", Desk.Error(Serve(Make("over"), Preview).Answer));
        Assert.Equal(before, _d.Files());
    }

    [Fact]
    public void Read_only_after_the_trial_answers_list_and_status_but_refuses_preview()
    {
        Assert.Equal(0, Serve(Make("over", readOnly: true), Status).Code);
        Assert.True(Serve(Make("over", readOnly: true), List).Answer["ok"]!.GetValue<bool>());
        var preview = Serve(Make("over", readOnly: true), Preview).Answer;
        Assert.Equal("needs_license", Desk.Error(preview));
    }

    [Fact]
    public void Orgodesk_has_no_ai_product_of_its_own_only_the_suite_bundle()
    {
        Assert.Equal("Orgodesk", Sales.Commands.ProductName);
        Assert.Equal(OrgoSuite.BundleProductId, Sales.Commands.BundleProductId);
        Assert.True(Sales.Premium.IsFreeForNow);                                // Pro free for now, through the kit's one gate
        Assert.False(Sales.Premium.IsOnSale);
#if DEBUG
        Assert.True(Sales.DevBuild);                                             // a Debug build is a development build (D-11)
#else
        Assert.False(Sales.DevBuild);
#endif
    }
}
