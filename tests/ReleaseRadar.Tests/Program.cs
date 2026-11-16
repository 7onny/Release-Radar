using System.Net;
using System.Text;
using System.Text.Json;
using ReleaseRadar.Core;

if (args.Contains("--refresh"))
{
    var path = args.Length > 1 ? args[1] : "data";
    var store = new CacheStore(Path.GetFullPath(path));
    var previous = await store.LoadAsync();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ReleaseRadar/1.0");
    var cache = await new SteamService(client).RefreshAsync(previous, Ranking.LondonDate(DateTimeOffset.UtcNow),
        new Progress<string>(Console.WriteLine), CancellationToken.None);
    await store.SaveAsync(cache);
    Console.WriteLine($"SAVED {cache.Games.Count} games to {store.CachePath}; {cache.Warnings.Count} warnings.");
    return;
}

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++; Console.WriteLine("PASS: " + label);
}
var day = new DateOnly(2026,11,17);
GameRelease Game(int id, int? rank, long count = 1000) => new() { AppId=id, Name="Game "+id, ReleaseDate=day, WishlistRank=rank, Followers=[new(day,count)] };
Check(Ranking.ForDay([Game(1,510),Game(2,437),Game(3,251),Game(4,670),Game(5,null,1000000)],day).Select(g=>g.AppId).SequenceEqual([3,2,1]),"Wishlist rank takes priority over followers; max three");
Check(Ranking.ForDay([Game(1,null,3000),Game(2,2000),Game(3,null,9000)],day).Select(g=>g.AppId).SequenceEqual([2,3,1]),"Follower fallback after ranked games");
Check(Ranking.Band(500)==RankBand.High && Ranking.Band(501)==RankBand.Middle && Ranking.Band(1500)==RankBand.Middle && Ranking.Band(1501)==RankBand.Lower && Ranking.Band(null)==RankBand.Unknown,"Exact colour boundaries");
var trendGame = Game(1,1) with { Followers=[new(day.AddDays(-7),2500),new(day,2750)] };
Check(Ranking.GetTrend(trendGame,day)?.IsTrending==true,"Trending inclusive thresholds");
Check(Ranking.GetTrend(trendGame with { Followers=[new(day.AddDays(-7),10000),new(day,10250)] },day)?.IsTrending==false,"Absolute gain alone cannot trigger flame");
Check(Ranking.GetTrend(trendGame with { Followers=[new(day.AddDays(-7),100),new(day,200)] },day)?.IsTrending==false,"Percentage alone cannot trigger flame");
Check(Ranking.GetTrend(trendGame,day.AddDays(1)) is null,"Stale observations never imply current growth");
Check(Ranking.GetTrend(trendGame with { Followers=[new(day.AddDays(-7),0),new(day,500)] },day) is null,"Zero baseline handled safely");
Check(Ranking.GetTrend(trendGame with { Followers=[new(day.AddDays(-6),2500),new(day,3000)] },day) is null,"Wrong observation interval rejected");
Check(Ranking.MonthCells(new(2026,11,1)).Count==42 && Ranking.MonthCells(new(2026,11,1))[0].DayOfWeek==DayOfWeek.Monday,"Six-week month starts Monday");
Check(Ranking.MonthCells(new(2028,2,1)).Contains(new(2028,2,29)),"Leap day present");
Check(Ranking.LondonDate(new DateTimeOffset(2026,7,1,23,30,0,TimeSpan.Zero))==new DateOnly(2026,7,2),"UK summer time date handling");
Check(SteamParser.ParseDate("17 Nov, 2026")==day && SteamParser.ParseDate("Nov 17, 2026")==day && SteamParser.ParseDate("Nov 2026") is null && SteamParser.ParseDate("Q1 2027") is null,"Only exact release dates get calendar slots");
var fixture = "<a data-ds-appid=\"123\" class=\"search_result_row ds_collapse_flag\"><span class=\"title\">Cats &amp; Dogs</span><div class=\"search_released responsive_secondrow\">17 Nov, 2026</div></a>";
var parsed = SteamParser.Parse(fixture,500).Single();
Check(parsed.AppId==123 && parsed.Position==501 && parsed.Name=="Cats & Dogs" && parsed.ReleaseDate==day,"HTML parsing preserves absolute page position and entities");
const string earlyAccessPage = "<div id=\"earlyAccessHeader\" class=\"early_access_header\">\n<h2 class=\"inset\">Coming Soon To Early Access</h2></div>";
Check(SteamParser.IsUpcomingEarlyAccessLaunch(earlyAccessPage), "Steam's upcoming Early Access heading confirms a launch");
Check(!SteamParser.IsUpcomingEarlyAccessLaunch(earlyAccessPage.Replace("Coming Soon To Early Access", "Early Access Game")) &&
    !SteamParser.IsUpcomingEarlyAccessLaunch("<h2>Coming Soon To Early Access</h2><p>Early Access</p>"),
    "Already playable games and unrelated Early Access text have no launch badge");
Check(SteamParser.ParseFollowers("<memberList><groupDetails><memberCount>42</memberCount></groupDetails><memberCount>999</memberCount></memberList>")==42,"Follower parsing uses game-hub count");
var exported = CalendarExport.Ics(Game(42,1) with { Name = "Cats, Dogs; & 🐈\n新" + new string('é',100) },DateTimeOffset.UtcNow);
Check(exported.Contains("DTSTART;VALUE=DATE:20261117\r\nDTEND;VALUE=DATE:20261118") && exported.Contains("UID:steam-42@release-radar.local"),"Outlook all-day event uses exclusive end date and stable UID");
Check(exported.Split("\r\n").All(l=>Encoding.UTF8.GetByteCount(l)<=75) && exported.Contains("Cats\\, Dogs\\;"),"ICS escapes text and folds UTF-8 without splitting characters");
Check(CalendarExport.YoutubeUrl("Cats & Dogs").EndsWith("Cats%20%26%20Dogs%20trailer"),"YouTube query safely encoded");
var temp = Path.Combine(Path.GetTempPath(),"release-radar-tests-"+Guid.NewGuid().ToString("N"));
var cacheStore = new CacheStore(temp);
try
{
    var original = new CalendarCache { UpdatedAt=DateTimeOffset.UtcNow, Games=[Game(42,1)] };
    await cacheStore.SaveAsync(original);
    Check((await cacheStore.LoadAsync())?.Games.Single().AppId==42,"Cache roundtrip");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await cacheStore.SaveAsync(original with { Games=[] }, cancelled.Token); } catch (OperationCanceledException) { }
    Check((await cacheStore.LoadAsync())?.Games.Count==1,"Interrupted write preserves existing cache");
    var upcomingDay = Ranking.LondonDate(DateTimeOffset.UtcNow).AddDays(1);
    var upcomingFixture = fixture.Replace("17 Nov, 2026", upcomingDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    using var mock = new HttpClient(new StubHandler(upcomingFixture, earlyAccessPage));
    var refreshed = await new SteamService(mock).RefreshAsync(original,upcomingDay,null,CancellationToken.None);
    Check(refreshed.Games.Count==1 && refreshed.Games[0].WishlistRank==1 && refreshed.Games[0].Followers.Count==1,"End-to-end refresh combines ranking, game filter and followers");
    await cacheStore.SaveAsync(refreshed);
    Check((await cacheStore.LoadAsync())?.Games.Single().IsEarlyAccessLaunch == true, "Confirmed badge survives cache reload");
    using var unavailableStorePage = new HttpClient(new StubHandler(upcomingFixture));
    var partial = await new SteamService(unavailableStorePage).RefreshAsync(refreshed, upcomingDay, null, CancellationToken.None);
    Check(!partial.Games.Single().IsEarlyAccessLaunch && partial.Games.Single().Followers.Count == 1 &&
        partial.Warnings.Any(w => w.StartsWith("Early Access status unavailable", StringComparison.Ordinal)),
        "Store lookup failure hides the badge and preserves the refreshed calendar and followers");
    using var broken = new HttpClient(new StubHandler(""));
    var rejected = false;
    try { await new SteamService(broken).RefreshAsync(original,day,null,CancellationToken.None); } catch (InvalidDataException) { rejected=true; }
    Check(rejected && (await cacheStore.LoadAsync())?.Games.Count==1,"Incomplete feed rejected without replacing cache");
}
finally { if (Directory.Exists(temp)) Directory.Delete(temp,true); }
Console.WriteLine($"\n{passed} checks passed.");

sealed class StubHandler(string fixture, string? storePage = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.StartsWith("/app/", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(storePage is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(storePage ?? "") });
        var content = request.RequestUri!.Host=="steamcommunity.com"
            ? "<memberList><groupDetails><memberCount>1000</memberCount></groupDetails></memberList>"
            : JsonSerializer.Serialize(new { success=1,total_count=1,results_html=fixture });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
    }
}
