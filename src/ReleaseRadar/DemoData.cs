using ReleaseRadar.Core;

namespace ReleaseRadar;

internal static class DemoData
{
    // An explicitly isolated visual fixture, never merged into the live cache.
    public static CalendarCache Create()
    {
        var rows = new (string Name, int Day, int Rank, long Followers, long Gain)[]
        {
            ("Trees Hate You",16,184,10599,300), ("Spirit Crossing",16,418,7260,969), ("Trafficity",16,428,5471,1687),
            ("Melvor Idle 2: A RuneScape Adventure",17,251,14348,619), ("Dawn of Defense",17,437,8415,1006), ("Milki Delivery",17,510,4096,268),
            ("Toto’s Journey",18,631,3873,939), ("MUV-LUV GIRLS GARDEN Mellow Days",18,1696,1503,357), ("Crackheads",18,2348,814,138),
            ("Grand Theft Timeline",19,302,4882,495), ("Lord of Undead",19,842,4904,695), ("Witch the Showdown",19,960,2393,384),
            ("Rock Band Simulator",20,901,2148,490), ("Lizard Slayer",20,1800,3461,33),
            ("Cat Cafe Manager 2: Big City Bliss",25,932,2323,140), ("Reshine",26,293,5215,427), ("Shark Mart",26,579,3151,193),
            ("Arkheron",27,175,22102,509), ("Am I Nima",27,237,9250,725), ("10 Million Pixels",27,414,3144,343)
        };
        return new CalendarCache
        {
            UpdatedAt = new DateTimeOffset(2026,11,16,12,0,0,TimeSpan.Zero), Source = "Planning sample · not live",
            Games = rows.Select((r, i) => new GameRelease { AppId = 90000000+i, Name = r.Name, ReleaseDate = new(2026,11,r.Day),
                WishlistRank = r.Rank, Followers = [new(new(2026,11,9),r.Followers-r.Gain),new(new(2026,11,16),r.Followers)] }).ToList()
        };
    }
}
