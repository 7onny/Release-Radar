# Release Radar

A native Windows calendar for anticipated Steam releases. Dark theme, monthly navigation, up to three releases per day, anticipation colours, evidence-based trending badges, Outlook export and YouTube trailer search.

## Run

Open `dist/ReleaseRadar/ReleaseRadar.exe`. Keep the accompanying DLL and JSON files together. Requires Windows 10/11 x64 and the .NET 8 **Desktop Runtime** (already present on the development machine). `dist/ReleaseRadar-Windows.zip` is the portable distribution; extract it before opening. No Steam login, API key or Microsoft account is required.

The first launch fetches Steam data. Afterwards, launch and month navigation use the saved data **without network requests**. Click **Refresh calendar** (or F5) to update it. Refresh reads the public wishlist catalogue, follower metrics and Early Access launch status for the selected month's displayed releases. It can take a few minutes; Cancel keeps your existing cache. A failed catalogue refresh never replaces good cached data. Follower and store page lookup failures are reported, and the release-date refresh can still succeed.

Data and window preferences live in `%LOCALAPPDATA%/ReleaseRadar`. Optional `--data-dir "path"` overrides that location. `--demo` opens a clearly labelled historical visual sample, with a separate data directory and no network traffic. Demo App IDs are placeholders and must not be used as real Steam links.

## Rules

- Games only, including Early Access; Steam's game filter excludes DLC, demos and software.
- Group exact release dates by day, take the best three wishlist positions. Rankless entries follow ranked entries, sorted by the latest known follower count, then title. There is no minimum popularity cutoff.
- Red: 1–500; orange: 501–1,500; muted blue: 1,501+. Unknown rank: neutral.
- EA marks an upcoming Early Access launch confirmed on its Steam store page. Refresh checks the selected month's upcoming tiles and caches the result for offline browsing. Unconfirmed launch types have no badge.
- Flame: at least 250 net followers **and** 10% growth over seven days. Growth uses the starting count. Observations must exist for the snapshot date and exactly seven days earlier. Missing or zero baselines yield no badge. No scheduled background collection occurs: refresh regularly to accumulate history.
- Relative monthly/quarterly/TBA dates are not invented: they appear in the unconfirmed list (top 20).
- Outlook action saves an all-day `.ics` file, then opens the system's calendar association. Choose Outlook if prompted; review and save/import there. It does not write directly to a Microsoft account. Exported reminders do not auto-update after release delays. The end date is exclusive and the event UID is stable per Steam app.
- Learn more opens YouTube search for the game title plus `trailer`.
- View on Steam opens the game's store page in the Steam desktop app.

## Data boundaries

Live data comes directly from Steam Store search and Steam Community game-hub counts, **not SteamDB**. The source is the public Top Wishlists catalogue, not an exhaustive list of every Steam release. Rank means position in the unfiltered Steam wishlist results retrieved with UK region and English language, before the games-only filter. Regional visibility, Steam search omissions, and catalogue changes during pagination may differ from SteamDB's reported global ranks. Counts are followers, not wishlists. The store's search endpoint is an undocumented public endpoint and may change; invalid or incomplete responses fail safely.

Only selected-month displayed games get fresh follower observations, avoiding thousands of unnecessary community requests. Changing months stays offline; click Refresh on a month to collect its follower history. Trends displayed from cache describe the cache snapshot date, not an unobserved current date. Old dated releases are retained after they disappear from upcoming results; historical months before first use are not backfilled. Future entries absent from a fresh feed are removed to avoid retaining stale dates.

No images are downloaded on startup, no analytics are collected and no credentials are stored. Cache writes use a temporary file followed by replacement.

## Build and test

Install a .NET 8 SDK, or use the workspace-local SDK in `.tools/dotnet`, then run:

```powershell
./build.ps1 -Test
```

The solution uses WPF and the .NET standard library with no third-party dependencies. `src/ReleaseRadar.Core` contains source parsing, ranking, history, cache and ICS logic; `src/ReleaseRadar` contains the Windows UI. `tests/ReleaseRadar.Tests` is a dependency-free executable test suite. Optional live integration check:

```powershell
./.tools/dotnet/dotnet.exe run --project tests/ReleaseRadar.Tests -c Release -- --refresh ./data
```

Keyboard: Tab/Enter for cards and actions; Escape closes details; Alt+Left/Right changes months; F5 refreshes. Standard Windows title-bar controls maximise and restore. The calendar grows with the window and scrolls at smaller sizes to keep release titles readable.
