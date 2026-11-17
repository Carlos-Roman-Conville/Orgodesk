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
}
