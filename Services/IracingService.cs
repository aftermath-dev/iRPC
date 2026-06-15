using irsdkSharp;

namespace iRPC;

public class IracingService : IDisposable
{
    private readonly IRacingSDK _sdk = new();
    private string? _lastYaml;
    private string _trackName = string.Empty;
    private string _trackConfig = string.Empty;
    private int _lastSessionNum = -1;
    private DateTime? _sessionStartUtc;

    public SessionData Poll()
    {
        var data = new SessionData();

        if (!_sdk.IsConnected())
            return data;

        data.IsConnected = true;
        data.IsOnTrack = GetBool("IsOnTrack");
        data.IsReplay = GetBool("IsReplay");

        int sessionNum = GetInt("SessionNum");
        int sessionFlags = GetInt("SessionFlags");
        data.Position = GetInt("PlayerCarPosition");
        data.CurrentLap = GetInt("Lap");
        data.LapsRemain = GetInt("SessionLapsRemain");
        data.TimeRemaining = GetFloat("SessionTimeRemain");
        data.IsCaution = (sessionFlags & 0xC000) != 0;   // Caution | CautionWaving
        data.IsCheckered = (sessionFlags & 0x0001) != 0;

        int carIdx = GetInt("PlayerCarIdx");

        string? yaml = _sdk.GetSessionInfo();
        if (!string.IsNullOrEmpty(yaml) && yaml != _lastYaml)
        {
            _lastYaml = yaml;
            RefreshStaticData(yaml, sessionNum);

            Logger.Log($"YAML update — Track={_trackName} Config={_trackConfig} " +
                       $"SessionType={IracingYaml.GetSessionValue(yaml, sessionNum, "SessionType")} " +
                       $"Car={IracingYaml.GetDriverValue(yaml, carIdx, "CarScreenNameShort")} " +
                       $"SessionNum={sessionNum} CarIdx={carIdx}");
            TrackCollector.Record(_trackName, _trackConfig);
        }

        if (sessionNum != _lastSessionNum)
        {
            _sessionStartUtc = DateTime.UtcNow;
            _lastSessionNum = sessionNum;
        }

        data.TrackName = _trackName;
        data.TrackConfig = _trackConfig;
        data.SessionStartUtc = _sessionStartUtc;

        if (_lastYaml is not null)
        {
            data.SessionType = FormatSessionType(IracingYaml.GetSessionValue(_lastYaml, sessionNum, "SessionType"));
            data.CarName = IracingYaml.GetDriverValue(_lastYaml, carIdx, "CarScreenNameShort") ?? string.Empty;
        }

        Logger.Log($"Poll — OnTrack={data.IsOnTrack} Replay={data.IsReplay} SessionType={data.SessionType} " +
                   $"Pos={data.Position} Lap={data.CurrentLap} LapsRemain={data.LapsRemain} " +
                   $"TimeRemain={data.TimeRemaining:F0}s Flags=0x{sessionFlags:X4} " +
                   $"Caution={data.IsCaution} Checkered={data.IsCheckered}");

        return data;
    }

    private void RefreshStaticData(string yaml, int sessionNum)
    {
        string? shortName = IracingYaml.GetValue(yaml, "TrackDisplayShortName");
        // Reject garbage values that contain a colon
        _trackName = (shortName != null && !shortName.Contains(':'))
            ? shortName
            : (IracingYaml.GetValue(yaml, "TrackDisplayName") ?? string.Empty);
        string? config = IracingYaml.GetValue(yaml, "TrackConfigName");
        _trackConfig = (config != null && !config.Contains(':')) ? config : string.Empty;
    }

    private static string FormatSessionType(string? raw)
    {
        if (raw is null) return string.Empty;
        return raw.ToLowerInvariant() switch
        {
            "practice" or "open practice" => "Practice",
            "qualify" or "lone qualify"   => "Qualify",
            "race" or "open race"         => "Race",
            "offline testing"             => "Test Drive",
            "time trial"                  => "Time Trial",
            _                             => raw
        };
    }

    private bool GetBool(string name)
    {
        try
        {
            return _sdk.GetData(name) switch
            {
                bool b      => b,
                bool[] arr  => arr.Length > 0 && arr[0],
                _           => false
            };
        }
        catch { return false; }
    }

    private int GetInt(string name)
    {
        try
        {
            return _sdk.GetData(name) switch
            {
                int i      => i,
                int[] arr  => arr.Length > 0 ? arr[0] : 0,
                uint u     => (int)u,
                uint[] arr => arr.Length > 0 ? (int)arr[0] : 0,
                _          => 0
            };
        }
        catch { return 0; }
    }

    private double GetFloat(string name)
    {
        try
        {
            return _sdk.GetData(name) switch
            {
                float f      => f,
                float[] arr  => arr.Length > 0 ? arr[0] : 0,
                double d     => d,
                double[] arr => arr.Length > 0 ? arr[0] : 0,
                _            => 0
            };
        }
        catch { return 0; }
    }

    public void Dispose() { }
}
