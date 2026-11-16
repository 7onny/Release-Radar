using System.Net;
using System.Text.Json;

namespace ReleaseRadar.Core;

public sealed class SteamService(HttpClient client)
{
    public async Task<CalendarCache> RefreshAsync(CalendarCache? previous, DateOnly visibleMonth,
        IProgress<string>? progress, CancellationToken ct)
    {
        // Keep the unfiltered position: filtering by game type before numbering would change rank bands.
        var ranked = await ReadSearchAsync(false, progress, ct);
        var gamesOnly = await ReadSearchAsync(true, progress, ct);
        var ranks = ranked.Where(r => r.AppId.HasValue).GroupBy(r => r.AppId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Position));
        var old = (previous?.Games ?? []).GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First());
        var now = DateTimeOffset.UtcNow;
        var today = Ranking.LondonDate(now);
        var games = gamesOnly.Where(r => r.AppId.HasValue).DistinctBy(r => r.AppId).Select(row => new GameRelease
        {
            AppId = row.AppId!.Value, Name = row.Name, ReleaseDate = row.ReleaseDate, ReleaseText = row.ReleaseText,
            WishlistRank = ranks.TryGetValue(row.AppId.Value, out var rank) ? rank : null,
            Followers = old.TryGetValue(row.AppId.Value, out var known)
                ? known.Followers.Where(f => f.Date >= today.AddDays(-45)).ToList() : []
        }).ToList();
        if (games.Count == 0) throw new InvalidDataException("Steam returned no games. Your saved calendar has been kept.");

        // Store only counts, never member IDs. Only fetch metrics for cards visible in the selected month.
        var picks = PicksForMonth(games, visibleMonth).ToList();
        var warnings = new List<string>();
        var failed = 0;
        for (var index = 0; index < picks.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var game = picks[index];
            progress?.Report($"Reading followers · {index + 1}/{picks.Count} · {game.Name}");
            try
            {
                var xml = await GetAsync($"https://steamcommunity.com/games/{game.AppId}/memberslistxml/?xml=1", ct);
                var count = SteamParser.ParseFollowers(xml);
                game.Followers.RemoveAll(f => f.Date == today);
                game.Followers.Add(new(today, count));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Xml.XmlException ||
                                        ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                failed++;
            }
            if (failed >= 5)
            {
                warnings.Add("Follower requests were stopped after repeated failures. Existing observations were retained.");
                break;
            }
            await Task.Delay(1200, ct);
        }
        if (failed > 0) warnings.Add($"Follower data unavailable for {failed} displayed games. Those games have no new trend observation.");

        // Use the final card selection in case fresh follower counts change the order of unranked games.
        var upcomingPicks = PicksForMonth(games, visibleMonth).Where(g => g.ReleaseDate >= today).ToList();
        var confirmed = await ReadEarlyAccessLaunchesAsync(upcomingPicks, warnings, progress, ct);
        var checkedIds = upcomingPicks.Select(g => g.AppId).ToHashSet();
        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var isEarlyAccessLaunch = checkedIds.Contains(game.AppId) ? confirmed.Contains(game.AppId)
                : old.TryGetValue(game.AppId, out var known) && known.ReleaseDate == game.ReleaseDate && known.IsEarlyAccessLaunch;
            if (isEarlyAccessLaunch) games[index] = game with { IsEarlyAccessLaunch = true };
        }
        // Preserve previously seen past releases, but replace/remove future listings so delays are respected.
        var ids = games.Select(g => g.AppId).ToHashSet();
        games.AddRange(old.Values.Where(g => g.ReleaseDate < today && !ids.Contains(g.AppId)));
        return new CalendarCache { UpdatedAt = now, Games = games, RankedProducts = ranked.Count, Warnings = warnings };
    }

    private static IEnumerable<GameRelease> PicksForMonth(List<GameRelease> games, DateOnly month) =>
        games.Where(g => g.ReleaseDate?.Year == month.Year && g.ReleaseDate?.Month == month.Month)
            .GroupBy(g => g.ReleaseDate!.Value).SelectMany(g => Ranking.ForDay(g, g.Key));

    private async Task<HashSet<int>> ReadEarlyAccessLaunchesAsync(List<GameRelease> picks, List<string> warnings,
        IProgress<string>? progress, CancellationToken ct)
    {
        var confirmed = new HashSet<int>();
        var failed = 0;
        for (var index = 0; index < picks.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var game = picks[index];
            progress?.Report($"Checking Early Access · {index + 1}/{picks.Count} · {game.Name}");
            try
            {
                var html = await GetAsync(game.StoreUrl + "?cc=gb&l=english", ct);
                if (SteamParser.IsUpcomingEarlyAccessLaunch(html)) confirmed.Add(game.AppId);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.RegularExpressions.RegexMatchTimeoutException ||
                                        ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                if (++failed >= 5)
                {
                    failed += picks.Count - index - 1;
                    break;
                }
            }
            if (index + 1 < picks.Count) await Task.Delay(1200, ct);
        }
        if (failed > 0) warnings.Add($"Early Access status unavailable for {failed} displayed games. Those games have no new badge.");
        return confirmed;
    }

    private async Task<List<SearchRow>> ReadSearchAsync(bool gamesOnly, IProgress<string>? progress, CancellationToken ct)
    {
        var all = new List<SearchRow>();
        var start = 0;
        var total = int.MaxValue;
        string? lastSignature = null;
        for (var page = 0; page < 200 && start < total; page++)
        {
            progress?.Report($"{(gamesOnly ? "Checking game releases" : "Reading wishlist rankings")} · {start:N0}{(total == int.MaxValue ? "" : $"/{total:N0}")}");
            var url = $"https://store.steampowered.com/search/results/?start={start}&count=100&filter=popularwishlist&infinite=1&cc=gb&l=english" + (gamesOnly ? "&category1=998" : "");
            var body = await GetAsync(url, ct);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ToString() is not ("1" or "True" or "true") ||
                !root.TryGetProperty("total_count", out var count) || !root.TryGetProperty("results_html", out var html))
                throw new InvalidDataException("Steam returned an unexpected response. Your saved calendar has been kept.");
            total = count.GetInt32();
            var rows = SteamParser.Parse(html.GetString() ?? "", start);
            if (rows.Count == 0 && start < total) throw new InvalidDataException("Steam returned an incomplete page. Please try Refresh later.");
            var signature = string.Join(",", rows.Select(r => $"{r.AppId}:{r.Name}"));
            if (signature == lastSignature) throw new InvalidDataException("Steam repeated a page. The previous calendar has been kept.");
            lastSignature = signature;
            all.AddRange(rows);
            start += rows.Count;
            // Steam limits rapid search pagination; keep refresh deliberately gentle.
            await Task.Delay(2200, ct);
        }
        if (start < total) throw new InvalidDataException("Steam's catalogue exceeded the safe page limit. The previous calendar has been kept.");
        return all;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            throw new HttpRequestException("Steam is temporarily limiting requests. Please try again later.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
