# Selling Orgodesk

How the paid version works, what you set up on Gumroad, and the copy for the store page and launch posts.
Written 2026-11-11 for the 10-day push (see the 10-Day Money Guide).

## How licensing works in the app

- **14-day trial** from the first run, everything unlocked. With 3 days or fewer left, a tray balloon says so,
  and the tray menu always shows "Trial: N days left: buy or enter a key...".
- **When the trial ends:** at the next start, Orgodesk shows the license window *before* it touches the desktop.
  Closing it without a key exits, and Windows shows its normal desktop icons. The buyer's Orgos and layout stay saved
  in `%APPDATA%\orgodesk` and come back as soon as a key is entered.
- **Key check:** the key from the Gumroad receipt goes into the license window (tray > License...). Orgodesk
  asks Gumroad's license API (`api.gumroad.com/v2/licenses/verify`) once, which counts one activation. The limit is
  **5 PCs per key**.
- **Rechecks:** about once a week in the background. **Offline is fine for 30 days** after the last successful check.
  A refunded, charged-back or disputed purchase stops working at the next recheck.
- **Setting the clock back** doesn't extend the trial (the app remembers the latest time it has seen).
- **Development build:** while `ProductId` in `src/Orgodesk.App/Sales.cs` is empty, there's no trial and no
  key, and everything is on. That's your own copy (`install.bat`). `release.bat` refuses to build until the id is set.
- Stored in `%APPDATA%\orgodesk\license.json`. Deleting it restarts the trial. That's acceptable for a $5 app,
  and stronger protection isn't worth the time this week.
- Code: `src/Orgodesk.Core/Licensing/` (rules, store, Gumroad check), `src/Orgodesk.App/UI/LicenseForm.cs`,
  the gate in `Program.cs`, the tray entries in `TrayContext.cs`. Tests: `tests/Orgodesk.Core.Tests/LicensingTests.cs`.

## Your steps (things only you can do)

1. **Gumroad account.** Sign up at gumroad.com, then add your payout method in Settings > Payments.
   Gumroad takes a fee on each sale (about 10% plus card fees at the time of writing; the sign-up page shows the current rate).
2. **Create the product.** New product > Digital product.
   - Name: `Orgodesk: desktop organizer for Windows`
   - Price: `$5` (launch price; raise it to $8 after the first week).
   - In the product's settings, turn on **License key: Generate a unique license key per sale**.
     Gumroad then shows the **product ID** in that license-key section. Copy it.
   - Upload the zip from step 4 as the file buyers get. Buyers can also use the free trial download.
3. **Paste three values** into `src/Orgodesk.App/Sales.cs` and save:
   - `ProductId` = the product ID from step 2
   - `BuyUrl` = the product's public link (looks like `https://<you>.gumroad.com/l/orgodesk`)
   - `SupportEmail` = the address you'll answer buyers from
4. **Build the release.** Run `release.bat`. It runs the tests and writes `release\Orgodesk.exe`, `THIRD-PARTY-NOTICES.txt` and `CHANGELOG.md`. Zip them
   with `README-buyers.txt` (below) as `Orgodesk.zip`.
5. **Test the full loop in your VM** before launch: fresh VM > run the exe > trial shows 14 days > make a
   test purchase at 100% off (Gumroad lets you create a discount code) > paste the key > "License activated".
   Then refund that test sale from Gumroad, wait for (or force) a recheck, and confirm the key stops working.
   To force a recheck, edit `LastVerifiedUtc` in `license.json` to a date 8 days back.
6. **Free trial download.** Put the same zip on the Gumroad page as a "Try it free for 14 days" link, or on itch.io as a
   free download. Each copy runs as a trial until a key is entered, so it's one file for both.
7. **Microsoft Store (optional, slower).** Partner Center > Apps and games > new app. Reserve the name
   "Orgodesk". The Store wants an MSIX package. It signs it, which removes the SmartScreen warning. Do this after
   the Gumroad launch, not before. Check the Store's current rules on in-app licensing through your own store first.

## Gumroad page copy

**Title:** Orgodesk: organize your Windows desktop into boxes

**Short line:** Group your desktop icons into labelled boxes that roll up, hide and switch with one click.

**Description:**

> Your desktop, sorted. Orgodesk turns your Windows 10/11 desktop into tidy, labelled boxes called Orgos.
>
> - **Orgos:** drag a box on the desktop, drop icons in, give it a title. Resize, move and snap them into place.
> - **Roll up:** double-click a title to fold an Orgo down to its title bar. Turn on auto-roll and it opens when you hover over it.
> - **Quick hide:** double-click empty desktop to hide every icon, and double-click again to bring them back.
> - **Folder portals:** an Orgo can show a live view of any folder (Downloads, a project folder) right on the desktop.
> - **Desktop pages:** several desktops' worth of Orgos; swipe or click to switch.
> - **Auto-sort rules:** new files land in the right Orgo by type or name.
> - **Presets and snapshots:** save a layout and switch back to it any time.
> - **Safe:** Orgodesk never moves or changes your files. Exit it and your normal desktop is back exactly as it was.
>
> **Try it free for 14 days**, everything included. One-time purchase, no subscription. A key works on up to 5 of your PCs.
>
> Requires Windows 10 or 11 (64-bit). Close any other desktop organizer before running it.

**Windows warning note (put it on the page and in the readme):**

> Windows may show "Windows protected your PC" the first time, because Orgodesk is from a new, independent developer.
> Click **More info**, then **Run anyway**. You only need to do this once.

## README-buyers.txt (ships in the zip)

```
Orgodesk - desktop organizer for Windows 10/11

Start:      double-click Orgodesk.exe. It runs from the tray (bottom right).
First Orgo: right-click and drag on empty desktop, or right-click the desktop > New Orgo here.
License:    tray icon > License... > paste the key from your Gumroad receipt > Activate.
Start with Windows: tray icon > Settings.
Exit:       tray icon > Exit. Your normal desktop icons come back.
Stuck?      Run  Orgodesk.exe --restore  to bring the normal desktop icons back.

Close Stardock Fences or any other desktop organizer before running Orgodesk.
If Windows says "Windows protected your PC": More info > Run anyway.
Support: <your support email>
```

## Launch posts

Read each subreddit's self-promotion rules first. Several allow it only on certain days or in a weekly thread.
Post a GIF, not just a link.

**r/Windows11 / r/software / r/desktops**

> **I made a desktop organizer for Windows 11. Free 14-day trial, $5 one-time**
>
> I got tired of a desktop full of icons, so I built Orgodesk: drag boxes ("Orgos") on the desktop, drop icons in,
> roll them up, quick-hide everything with a double-click, and keep folder portals and multiple desktop pages.
> [GIF]
> It doesn't move your files; exiting gives you your normal desktop back. Trial is fully unlocked.
> Feedback and bug reports very welcome: I'm fixing things daily this week. Link: <BuyUrl>

**AlternativeTo:** add Orgodesk as an alternative to Stardock Fences, with the short line and the screenshots.
Tags: desktop organizer, desktop icons, Windows.

**Product Hunt (day 8):** tagline "Tidy Windows desktop icons into boxes that roll up and hide", with the GIF
as the first image.

Say "an alternative to Fences" only in comparisons. Never use Stardock's logo or screenshots, and don't imply any link
to them. One line in the description is enough: "Orgodesk is independent and not affiliated with Stardock."

## Screenshots and GIF to capture (in the VM)

1. A messy desktop next to the same desktop organized into 4 Orgos.
2. An Orgo rolling up and peeking open (GIF, about 5 seconds).
3. Quick hide: double-click, icons gone, double-click, icons back (GIF).
4. A folder portal showing Downloads.
5. The tray menu and the Settings window.

Use a clean wallpaper and fake file names, never your real desktop.
