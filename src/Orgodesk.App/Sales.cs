using Orgodesk.Core.Licensing;

namespace Orgodesk.App;

/// <summary>
/// Sales settings for this release. Fill ProductId and BuyUrl from the Gumroad product page
/// (Product > Content > License key: "Generate a unique license key per sale", then the product id shown there).
/// Leave ProductId empty for a development build: no trial, no key, everything on.
/// release.bat refuses to build while ProductId is empty.
/// </summary>
internal static class Sales
{
    public const string ProductId = "kLLRtCRswECEEILkxXWhIg==";
    public const string BuyUrl = "https://rexochango.gumroad.com/l/orgodesk";
    public const string SupportEmail = "support@crc-solutions.org";
    /// <summary>
    /// Address of the version file Orgodesk reads once a day to say "a new version is out", e.g.
    /// {"version":"1.0.1","url":"https://.../download page","notes":"What changed"}. Must be https.
    /// Empty = no update check and no update entries in the menus.
    /// </summary>
    public const string UpdateUrl = "";

    public static readonly SalesConfig Config = new(ProductId, BuyUrl);

    /// <summary>
    /// Pro: Orgodesk has no Pro (App Premium) tier, and that is on purpose for now. Every feature, and every command
    /// action, is part of the app; nothing answers needs_pro (Rex 2026-11-23: premium is decided app by app later,
    /// CONTROLLER-HANDOFF "premium decided app by app"). When Rex decides one, it goes here as crc-appshell's SalesConfig.
    /// </summary>
    // shortcut: until the kit flag lands (crc-appshell SalesConfig ProFreeForNow, branch pro-free-for-now); then use that one gate
    public const bool ProIsFreeForNow = true;

    /// <summary>
    /// The AI connection (suite PLAN.md amendment "AI only in the suite bundle"; crc-appshell DESIGN.md D-3 to D-9 and
    /// D-11, kit-1.10): command mode needs the suite bundle's key or the suite's 30-day trial, then is read-only (list and
    /// status). Orgodesk has no AI product of its own: the bundle's id lives in the kit (<c>OrgoSuite.BundleProductId</c>).
    /// The app's window and tray never ask it.
    /// </summary>
    public static readonly Crc.AppShell.Commands.CommandSales Commands = new("Orgodesk", DevBuild);

#if DEBUG
    /// <summary>A Debug build is a development build: command mode open, no AI-connection check (D-11). Never ship one.</summary>
    public const bool DevBuild = true;
#else
    public const bool DevBuild = false;
    // D-9 as amended by D-11 (Rex 2026-11-23): a Release build stops here (CS0020, division by constant zero) while the
    // suite bundle's product id is empty, so a release never ships the AI connection free. Fill it in (Gumroad, in the kit).
    private const int ReleaseNeedsTheBundleId = 1 / (Crc.AppShell.Commands.OrgoSuite.BundleProductId == "" ? 0 : 1);
#endif
}
