using System.Security.Cryptography;
using System.Text;
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

    private static readonly byte[] _sigKey = Encoding.UTF8.GetBytes("iRPC\x01\x9f\x4a\xb2\x77\xe3\x0c\xd5\x88\x3f\xa6\x51\x2e\x9b\xc4\x70");

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

    private static readonly string BackupPath = FilePath + ".bak";
    private static readonly string CorruptPath = FilePath + ".corrupt";

    private static StatsData Load()
    {
        if (!File.Exists(FilePath)) return new();

        if (TryLoadFrom(FilePath, out var data, out string? failure))
            return data!;

        LogLoadFailure(FilePath, failure!);

        // Main file is bad — fall back to the last known-good backup before giving up.
        if (File.Exists(BackupPath) && TryLoadFrom(BackupPath, out data, out failure))
        {
            LogLoadFailure(BackupPath, "recovered from backup after main file failed");
            return data!;
        }
        if (File.Exists(BackupPath))
            LogLoadFailure(BackupPath, failure!);

        // Preserve the unreadable file for inspection instead of silently overwriting it.
        try { File.Copy(FilePath, CorruptPath, overwrite: true); } catch { }

        return new();
    }

    private static bool TryLoadFrom(string path, out StatsData? data, out string? failure)
    {
        data = null;
        failure = null;
        try
        {
            string raw = File.ReadAllText(path);

            // Legacy plain JSON (pre-signature) — accept once, will be re-saved signed.
            if (raw.TrimStart().StartsWith('{') && !raw.Contains("\"d\""))
            {
                data = JsonSerializer.Deserialize<StatsData>(raw) ?? new();
                return true;
            }

            using var doc = JsonDocument.Parse(raw);
            string dataB64 = doc.RootElement.GetProperty("d").GetString() ?? "";
            string storedSig = doc.RootElement.GetProperty("s").GetString() ?? "";

            byte[] dataBytes = Convert.FromBase64String(dataB64);
            string expectedSig = Sign(dataBytes);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(storedSig),
                    Convert.FromHexString(expectedSig)))
            {
                failure = "signature mismatch";
                return false;
            }

            data = JsonSerializer.Deserialize<StatsData>(dataBytes) ?? new();
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    // Written independent of Debug Mode/Logger.Enabled so a load failure leaves a trace even
    // when the user never turned on debug logging.
    private static void LogLoadFailure(string path, string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath + ".load-errors.log",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {path}: {reason}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Save(StatsData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            byte[] dataBytes = JsonSerializer.SerializeToUtf8Bytes(data);
            string dataB64   = Convert.ToBase64String(dataBytes);
            string sig       = Sign(dataBytes);

            string json = JsonSerializer.Serialize(new { d = dataB64, s = sig });
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Roll the current (about-to-be-replaced) good file into the backup slot first,
            // so a failed read always has a known-good fallback to recover from.
            if (File.Exists(FilePath))
                File.Copy(FilePath, BackupPath, overwrite: true);

            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogLoadFailure(FilePath, "save failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Sign(byte[] data)
    {
        using var hmac = new HMACSHA256(_sigKey);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }
}
