# Legal drafts for Orgodesk

**Drafts, not legal advice.** Written 2026-11-12 from what the app actually does (checked in the code). Rex fills the
`[BRACKETS]`, decides the open choices, and decides whether a lawyer reviews them before release
(HQ/testing/PRODUCTION.md section 8).

| File | Ships where |
|---|---|
| [EULA.md](EULA.md) | In the zip as `EULA.txt` (release.bat copies it) and linked on the Gumroad page. |
| [PRIVACY.md](PRIVACY.md) | On the Gumroad page (or a page it links to). |
| [REFUNDS.md](REFUNDS.md) | On the Gumroad page. |

Facts the drafts rely on (re-check them if the code changes):
- Layout, settings, license record and logs are stored only in `%APPDATA%\orgodesk` on the user's PC.
- The only network traffic is the license check: the license key and product id go to `api.gumroad.com` when a key is
  activated and about once a week after that. No analytics, no crash upload, no ads.
- A key works on up to 5 PCs (`SalesConfig.MaxActivations`); 14-day trial; 30 days offline before a recheck is needed.
