using System.Text.Json;

namespace iRPC;

public static class SRatingTracker
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "sr_history.json");

    private const int MaxHistory = 200;

    private static readonly object _lock = new();
    private static readonly SRatingHistory _data = Load();
    private static bool _wasCheckered;

    public static void Record(SessionData data)
    {
        bool checkeredNow = data.IsConnected && data.SessionType == "Race" && data.IsCheckered;

        if (checkeredNow && !_wasCheckered && data.PlayerSRating > 0)
        {
            lock (_lock)
            {
                _data.RaceEndSRatings.Add(data.PlayerSRating);
                if (_data.RaceEndSRatings.Count > MaxHistory)
                    _data.RaceEndSRatings.RemoveAt(0);
                Save(_data);
            }
        }

        _wasCheckered = checkeredNow;
    }

    public static float AverageOfLast(int count)
    {
        if (count <= 0) return 0;
        lock (_lock)
        {
            if (_data.RaceEndSRatings.Count == 0) return 0;
            int skip = Math.Max(0, _data.RaceEndSRatings.Count - count);
            return (float)Math.Round(_data.RaceEndSRatings.Skip(skip).Average(), 2);
        }
    }

    private static SRatingHistory Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SRatingHistory>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void Save(SRatingHistory data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
