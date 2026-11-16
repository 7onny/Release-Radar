using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ReleaseRadar.Core;

public sealed record SearchRow(int? AppId, string Name, string ReleaseText, DateOnly? ReleaseDate, int Position);

public static class SteamParser
{
    private static MatchCollection Matches(string input, string pattern) => Regex.Matches(input, pattern,
        RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(3));
    private static string Text(string input) => WebUtility.HtmlDecode(Regex.Replace(input, "<[^>]+>", " ")).Trim();

    public static List<SearchRow> Parse(string html, int start)
    {
        var rows = new List<SearchRow>();
        foreach (Match anchor in Matches(html, "<a\\b[^>]*class=\"[^\"]*search_result_row[^\"]*\"[^>]*>.*?</a>"))
        {
            var block = anchor.Value;
            var idMatch = Matches(block, "data-ds-appid=\"(\\d+)\"").FirstOrDefault();
            var name = Matches(block, "<span[^>]*class=\"title\"[^>]*>(.*?)</span>").FirstOrDefault();
            var release = Matches(block, "<div[^>]*class=\"search_released[^\"]*\"[^>]*>(.*?)</div>").FirstOrDefault();
            if (name is null || release is null) throw new InvalidDataException("Steam changed its search format; the saved calendar has been kept.");
            var releaseText = Text(release.Groups[1].Value);
            rows.Add(new(idMatch is null ? null : int.Parse(idMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                Text(name.Groups[1].Value), releaseText, ParseDate(releaseText), start + rows.Count + 1));
        }
        return rows;
    }

    public static bool IsUpcomingEarlyAccessLaunch(string html) => Matches(html,
        "<div\\b[^>]*\\bid=\"earlyAccessHeader\"[^>]*>\\s*<h2\\b[^>]*>\\s*Coming Soon To Early Access\\s*</h2>").Count > 0;

    public static DateOnly? ParseDate(string text)
    {
        string[] formats = ["d MMM, yyyy", "dd MMM, yyyy", "MMM d, yyyy", "d MMM yyyy", "MMM dd, yyyy", "yyyy-MM-dd"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
    }

    public static long ParseFollowers(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var doc = XDocument.Load(reader);
        // groupDetails is the public game-hub count; the root count can include stale members.
        var value = doc.Root?.Element("groupDetails")?.Element("memberCount")?.Value;
        if (!long.TryParse(value, out var count) || count < 0) throw new InvalidDataException("Follower count unavailable.");
        return count;
    }
}
