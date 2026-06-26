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

    private static int    _prevLap       = -1;
    private static int    _prevIncidents = -1;
    private static string _prevTrack     = "";

    public static void Record(SessionData data)
    {
        if (!data.IsConnected || !data.IsOnTrack || data.IsReplay) return;

        lock (_lock)
        {
            if (data.SessionType.Length > 0) Increment(_data.SecondsBySessionType, data.SessionType);
            if (data.CarName.Length > 0) Increment(_data.SecondsByCar, data.CarName);
            if (data.TrackName.Length > 0) Increment(_data.SecondsByTrack, data.TrackName);
            _data.TotalSeconds++;

            // Reset per-session counters when the track changes (new session).
            if (data.TrackName != _prevTrack)
            {
                _prevTrack     = data.TrackName;
                _prevLap       = data.CurrentLap;
                _prevIncidents = data.IncidentCount;
            }

            // Laps — count forward jumps in CurrentLap only.
            if (_prevLap >= 0 && data.CurrentLap > _prevLap)
            {
                ulong delta = (ulong)(data.CurrentLap - _prevLap);
                _data.TotalLaps += delta;
                if (data.TrackName.Length > 0) Increment(_data.LapsByTrack, data.TrackName, delta);
            }
            _prevLap = data.CurrentLap;

            // Distance — Speed is m/s, poll is 1 Hz, so each tick ≈ Speed metres.
            _data.TotalDistanceM += (ulong)data.Speed;

            // Incidents — cumulative per session; add positive deltas only.
            if (_prevIncidents >= 0 && data.IncidentCount > _prevIncidents)
                _data.TotalIncidents += (ulong)(data.IncidentCount - _prevIncidents);
            _prevIncidents = data.IncidentCount;

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

    private static void Increment(Dictionary<string, ulong> map, string key, ulong delta = 1) =>
        map[key] = map.GetValueOrDefault(key) + delta;

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
