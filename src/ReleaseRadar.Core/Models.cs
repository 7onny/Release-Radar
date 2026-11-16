namespace ReleaseRadar.Core;

public sealed record FollowerObservation(DateOnly Date, long Count);

public sealed record GameRelease
{
    public int AppId { get; init; }
    public string Name { get; init; } = "";
    public DateOnly? ReleaseDate { get; init; }
    public string ReleaseText { get; init; } = "Date unconfirmed";
    public int? WishlistRank { get; init; }
    public bool IsEarlyAccessLaunch { get; init; }
    public List<FollowerObservation> Followers { get; init; } = [];
    public string StoreUrl => $"https://store.steampowered.com/app/{AppId}/";
}

public sealed record CalendarCache
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAt { get; init; }
    public List<GameRelease> Games { get; init; } = [];
    public int RankedProducts { get; init; }
    public string Source { get; init; } = "Steam Store · UK catalogue";
    public List<string> Warnings { get; init; } = [];
}

public enum RankBand { High, Middle, Lower, Unknown }
public sealed record Trend(long Gain, double Percent, DateOnly AsOf)
{
    public bool IsTrending => Gain >= 250 && Percent >= 10;
}

public static class Ranking
{
    public static RankBand Band(int? rank) => rank switch
    {
        > 0 and <= 500 => RankBand.High,
        <= 1500 and > 500 => RankBand.Middle,
        > 1500 => RankBand.Lower,
        _ => RankBand.Unknown
    };

    public static IEnumerable<GameRelease> Order(IEnumerable<GameRelease> games) => games
        .OrderBy(g => g.WishlistRank is > 0 ? g.WishlistRank : int.MaxValue)
        .ThenByDescending(g => g.Followers.OrderByDescending(f => f.Date).FirstOrDefault()?.Count ?? -1)
        .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.AppId);

    public static List<GameRelease> ForDay(IEnumerable<GameRelease> games, DateOnly day) =>
        Order(games.Where(g => g.ReleaseDate == day)).DistinctBy(g => g.AppId).Take(3).ToList();

    // Require observations on both dates: never relabel a 6- or 10-day change as seven days.
    public static Trend? GetTrend(GameRelease game, DateOnly asOf)
    {
        var current = game.Followers.LastOrDefault(f => f.Date == asOf);
        var previous = game.Followers.LastOrDefault(f => f.Date == asOf.AddDays(-7));
        if (current is null || previous is null || previous.Count <= 0) return null;
        var gain = current.Count - previous.Count;
        return new(gain, 100d * gain / previous.Count, asOf);
    }

    public static DateOnly LondonDate(DateTimeOffset time) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")).DateTime);

    public static List<DateOnly> MonthCells(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var count = ((offset + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7) * 7;
        return Enumerable.Range(0, count).Select(i => first.AddDays(i - offset)).ToList();
    }
}
