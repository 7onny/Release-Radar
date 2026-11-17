# Release Radar

A native Windows calendar for anticipated Steam releases. See the top three upcoming games per day, identify Early Access and 1.0 launches, and open a game's Steam store page from its tile.

[![Windows CI](https://github.com/7onny/Release-Radar/actions/workflows/windows-ci.yml/badge.svg)](https://github.com/7onny/Release-Radar/actions/workflows/windows-ci.yml)

- Wishlist-rank colours and seven-day follower trends from public Steam data.
- All-day Outlook calendar export and YouTube trailer search.
- Offline browsing after the first refresh, with the calendar stored locally.

## Run

On Windows 10 or 11 (x64), [install the .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then clone or download this repository and run `./build.ps1` in PowerShell. Open **Launch Release Radar.cmd** from the repository, or run the published executable directly.

The build creates `dist/ReleaseRadar-Windows.zip`. Extract the entire archive on the destination computer before opening `ReleaseRadar.exe`. Running a built copy requires the .NET 8 **Desktop Runtime**. The calendar can fetch Steam data without a Steam login, API key or Microsoft account.

The first launch fetches Steam data. Afterwards, launch and month navigation use the saved data **without network requests**. Click **Refresh calendar** (or F5) to update it. Refresh reads the public wishlist catalogue, follower metrics and Early Access launch status for the selected month's displayed releases. It can take a few minutes; Cancel keeps your existing cache. A failed catalogue refresh never replaces good cached data. Follower and store page lookup failures are reported, and the release-date refresh can still succeed.

Data and window preferences live in `%LOCALAPPDATA%/ReleaseRadar` when running the executable directly. The repository launcher uses the local `data` folder. Pass `--data-dir "path"` to choose a location. Use `--demo` to try a historical sample with a separate data directory and no network traffic.

## Rules

- Games only, including Early Access; Steam's game filter excludes DLC, demos and software.
- Group exact release dates by day, take the best three wishlist positions. Rankless entries follow ranked entries, sorted by the latest known follower count, then title. There is no minimum popularity cutoff.
- Red: 1–500; orange: 501–1,500; muted blue: 1,501+. Unknown rank: neutral.
- EA marks an upcoming Early Access launch confirmed on its Steam store page. Refresh checks the selected month's upcoming tiles and caches the result for offline browsing. Unconfirmed launch types have no badge.
- 1.0 marks a full release from Early Access on the exact date in Steam's Leaving Early Access section. Refresh checks previously tracked Early Access launches and 1.0 plans, plus up to 50 games from Steam's Early Access search. The usual daily ranking applies. Coverage is limited to those candidates, and a vague date does not create a tile.
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

Run the automated checks and package the app in one step:

```powershell
./build.ps1 -Test
```

The solution uses WPF and the .NET standard library with no third-party dependencies. `src/ReleaseRadar.Core` contains source parsing, ranking, history, cache and ICS logic; `src/ReleaseRadar` contains the Windows UI. `tests/ReleaseRadar.Tests` is a dependency-free executable test suite. Optional live integration check:

```powershell
dotnet run --project tests/ReleaseRadar.Tests -c Release -- --refresh ./data
```

Keyboard: Tab/Enter for cards and actions; Escape closes details; Alt+Left/Right changes months; F5 refreshes. Standard Windows title-bar controls maximise and restore. The calendar grows with the window and scrolls at smaller sizes to keep release titles readable.

## Feedback

Use [GitHub Issues](https://github.com/7onny/Release-Radar/issues) for bugs and suggestions. If a listing looks wrong, include the game's Steam URL and the month displayed in the calendar. See the [security policy](SECURITY.md) for private vulnerability reporting.
