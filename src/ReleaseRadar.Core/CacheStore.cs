using System.Text.Json;

namespace ReleaseRadar.Core;

public sealed class CacheStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string CachePath => Path.Combine(DirectoryPath, "calendar.json");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    public async Task<CalendarCache?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(CachePath)) return null;
        await using var stream = File.OpenRead(CachePath);
        var cache = await JsonSerializer.DeserializeAsync<CalendarCache>(stream, JsonOptions, ct);
        if (cache is null || cache.SchemaVersion != 1 || cache.Games is null || cache.Warnings is null ||
            cache.Games.Any(g => g is null || g.AppId <= 0 || string.IsNullOrWhiteSpace(g.Name) || g.Followers is null))
            throw new InvalidDataException("The saved calendar is invalid. Refresh to rebuild it.");
        return cache;
    }

    public async Task SaveAsync(CalendarCache cache, CancellationToken ct = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(cache, JsonOptions), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, CachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
