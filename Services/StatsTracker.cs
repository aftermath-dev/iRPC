using System.Text.Json;

namespace iRPC;

// Accumulates time-on-track in 1-second increments (one call per poll tick) and persists to
// %AppData%\iRPC\stats.json, so SettingsWindow/StatsWindow can show totals and favorites
// without needing a database.
public static class StatsTracker
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "stats.json");

    private static readonly object _lock = new();
    private static readonly StatsData _data = Load();
    private static int _ticksSinceSave;

    public static void Record(SessionData data)
    {
        if (!data.IsConnected || !data.IsOnTrack || data.IsReplay) return;

        lock (_lock)
        {
            if (data.SessionType.Length > 0) Increment(_data.SecondsBySessionType, data.SessionType);
            if (data.CarName.Length > 0) Increment(_data.SecondsByCar, data.CarName);
            if (data.TrackName.Length > 0) Increment(_data.SecondsByTrack, data.TrackName);
            _data.TotalSeconds++;

            // Avoid hitting disk every second — flush periodically and rely on Flush() at exit
            // to catch the last partial window.
            if (++_ticksSinceSave >= 30)
            {
                _ticksSinceSave = 0;
                Save(_data);
            }
        }
    }

    public static StatsData Snapshot()
    {
        lock (_lock) return _data.Clone();
    }

    public static void Flush()
    {
        lock (_lock) Save(_data);
    }

    private static void Increment(Dictionary<string, long> map, string key) =>
        map[key] = map.GetValueOrDefault(key) + 1;

    private static StatsData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StatsData>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void Save(StatsData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
