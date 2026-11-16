using System.Globalization;
using System.Text;

namespace ReleaseRadar.Core;

public static class CalendarExport
{
    public static string YoutubeUrl(string title) =>
        "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(title + " trailer");

    public static string Ics(GameRelease game, DateTimeOffset now)
    {
        if (game.ReleaseDate is not { } date) throw new InvalidOperationException("A confirmed release date is required.");
        var lines = new[] {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Release Radar//Games Calendar//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
            "BEGIN:VEVENT", $"UID:steam-{game.AppId}@release-radar.local",
            "DTSTAMP:" + now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            "DTSTART;VALUE=DATE:" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "DTEND;VALUE=DATE:" + date.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "SUMMARY:" + Escape(game.Name + " — Steam release"),
            "DESCRIPTION:" + Escape("Release date listed by Steam. Dates may change; this imported event does not update automatically.\n" + game.StoreUrl),
            "URL:" + game.StoreUrl, "TRANSP:TRANSPARENT", "END:VEVENT", "END:VCALENDAR"
        };
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r\n", "\n")
        .Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");

    private static string Fold(string line)
    {
        var result = new StringBuilder(); var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); octets = 1; }
            result.Append(rune); octets += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
