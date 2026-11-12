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
    public const string ProductId = "";
    public const string BuyUrl = "";
    public const string SupportEmail = "support@crc-solutions.org";

    public static readonly SalesConfig Config = new(ProductId, BuyUrl);
}
