# Legal drafts for Orgodesk

**Drafts, not legal advice, not reviewed by a lawyer.** Rewritten 2026-11-12 from common practice and from what the
app actually does (checked in the code). Whether a lawyer reviews them before release is Rex's call
(HQ/testing/PRODUCTION.md section 8).

| File | Ships where |
|---|---|
| [EULA.md](EULA.md) | In the zip as `EULA.txt` (release.bat copies it) and linked on the Gumroad page. |
| [PRIVACY.md](PRIVACY.md) | On the Gumroad page (or a page it links to). |
| [REFUNDS.md](REFUNDS.md) | On the Gumroad page. Matches Gumroad's "30-day money-back guarantee" account setting. |

## Blanks for Rex (search for the square brackets)

| Blank | Where | Notes |
|---|---|---|
| `[SELLER LEGAL NAME]` | all three | Your own name, or CRC Solutions if it sells this. |
| `[STATE]` | EULA, PRIVACY | The US state whose law governs (where you or the business are based). |
| `[SUPPORT EMAIL]` | all three, README-buyers.txt, Sales.cs | One address you will actually read. |
| `[DATE]` | all three | The day the product page goes live. |
| `[2]` years for support emails | PRIVACY | Change if you keep them longer or shorter. |
| Update emails | PRIVACY ("Your purchase") | Say yes or no to emailing buyers about new versions. |

## What the drafts are built from

- **EULA:** the clauses standard software EULAs carry (license grant and scope, restrictions, ownership, updates,
  third-party components, warranty disclaimer, limitation of liability, termination, governing law, severability,
  entire agreement), plus a consumer-rights carve-out so the disclaimers don't claim more than consumer law allows.
- **Privacy policy:** the items GDPR Article 13 asks for (who the controller is, what data, why and on what legal
  basis, who receives it, transfers abroad, how long it is kept, the person's rights and the right to complain) and
  California's CalOPPA items (effective date, Do Not Track statement, how changes are announced). The CCPA's extra
  duties apply only above its size thresholds, which a new one-person seller is far below.
- **Refund policy:** Gumroad lets sellers choose between a 30-day money-back guarantee and no refunds at account
  level; 30 days is used here because a no-refund stance doesn't stop chargebacks, and too many chargebacks put the
  Gumroad account at risk. It also covers the Smart App Control case, because Orgodesk ships unsigned for now.

## Facts the drafts rely on (re-check if the code changes)

- Layout, settings, license record and logs are stored only in `%APPDATA%\orgodesk`.
- A hash of the license key is kept in `HKCU\Software\Orgodesk` (local only, survives uninstall on purpose).
- The only network traffic is the license check: key and product id to `api.gumroad.com` at activation and about
  weekly. No analytics, no crash upload, no ads.
- 5 PCs per key (`SalesConfig.MaxActivations`); 14-day trial; 30 days offline before a recheck is needed.
- The uninstaller offers to delete the data folder; it does not remove the registry hash.
