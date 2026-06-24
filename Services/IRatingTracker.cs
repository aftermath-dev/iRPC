using System.Text.Json;

namespace iRPC;

// Records the player's iRating at the end of each Race session (detected via the checkered-flag
// false->true edge) and persists it to %AppData%\iRPC\irating_history.json, so we can show a
// trend ("average of last X races") rather than just the live, single-race iRating value.
public static class IRatingTracker
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "irating_history.json");

    // Caps disk/memory growth — far more than anyone needs for a "last X races" average.
    private const int MaxHistory = 200;

    private static readonly object _lock = new();
    private static readonly IRatingHistory _data = Load();
    private static bool _wasCheckered;

    public static void Record(SessionData data)
    {
        bool checkeredNow = data.IsConnected && data.SessionType == "Race" && data.IsCheckered;

        if (checkeredNow && !_wasCheckered && data.PlayerIRating > 0)
        {
            lock (_lock)
            {
                _data.RaceEndIRatings.Add(data.PlayerIRating);
                if (_data.RaceEndIRatings.Count > MaxHistory)
                    _data.RaceEndIRatings.RemoveAt(0);
                Save(_data);
            }
        }

        _wasCheckered = checkeredNow;
    }

    // Average of the most recent 'count' recorded race-end iRatings; 0 if there's no history yet.
    public static int AverageOfLast(int count)
    {
        if (count <= 0) return 0;
        lock (_lock)
        {
            if (_data.RaceEndIRatings.Count == 0) return 0;
            int skip = Math.Max(0, _data.RaceEndIRatings.Count - count);
            return (int)Math.Round(_data.RaceEndIRatings.Skip(skip).Average());
        }
    }

    private static IRatingHistory Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<IRatingHistory>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void Save(IRatingHistory data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
