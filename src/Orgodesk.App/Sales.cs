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
    /// Pro (App Premium): Orgodesk has no Pro tier and no Pro product yet, and says so here on purpose (Rex 2026-11-23: premium
    /// is decided app by app later; crc-appshell D-13, CA-3 option b). Every feature, and every command action, is part of the
    /// app; nothing answers needs_pro. The one gate is the kit's: <see cref="Premium"/>.<c>IsFreeForNow</c>. When Rex decides a
    /// Pro, its Gumroad id goes into <see cref="ProProductId"/> and this flag goes.
    /// </summary>
    public const bool ProFreeForNow = true;
    public const string ProProductId = "";
    public static readonly Crc.AppShell.Licensing.SalesConfig Premium =
        new(ProProductId, "", ProductName: "Orgodesk Pro", DevBuild: DevBuild, ProFreeForNow: ProFreeForNow);

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
    // D-12 (CA-3): the same for Pro: a Release build stops here unless Pro has a product id or says, in ProFreeForNow, that it is free.
    private const int ReleaseNeedsTheProIdOrTheFreeLine = 1 / (ProProductId == "" && !ProFreeForNow ? 0 : 1);
#endif
}
