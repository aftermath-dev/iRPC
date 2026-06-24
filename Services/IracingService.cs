using irsdkSharp;

namespace iRPC;

public class IracingService : IDisposable
{
    private readonly IRacingSDK _sdk = new();
    private string? _lastYaml;
    private string _trackName = string.Empty;
    private string _trackConfig = string.Empty;
    private string _trackCodeName = string.Empty;
    private string _carCodeName = string.Empty;
    private int _strengthOfField;
    private int _lastSessionNum = -1;
    private DateTime? _sessionStartUtc;

    // iRacing exiting/crashing mid-read can throw out of irsdkSharp (e.g. a stale shared-memory
    // handle) rather than just having IsConnected() go false. Without this, an exception here
    // would skip the DiscordService.Update call entirely for that tick, leaving a stale presence
    // showing instead of clearing it.
    public SessionData Poll()
    {
        try
        {
            return PollInternal();
        }
        catch (Exception ex)
        {
            Logger.Log($"Poll failed, resetting connection state: {ex}");
            _lastYaml = null;
            _lastSessionNum = -1;
            _sessionStartUtc = null;
            return new SessionData();
        }
    }

    private SessionData PollInternal()
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
        data.ClassPosition = GetInt("PlayerCarClassPosition");
        data.CurrentLap = GetInt("Lap");
        data.LapsRemain = GetInt("SessionLapsRemain");
        data.TimeRemaining = GetFloat("SessionTimeRemain");
        data.IsCaution = (sessionFlags & 0xC000) != 0;   // Caution | CautionWaving
        data.IsCheckered = (sessionFlags & 0x0001) != 0;
        data.Speed = (float)GetFloat("Speed");
        data.FuelLevel = (float)GetFloat("FuelLevel");
        data.FuelPercent = (float)GetFloat("FuelLevelPct");
        data.OnPitRoad = GetBool("OnPitRoad");
        data.LastLapTime = (float)GetFloat("LapLastLapTime");
        data.BestLapTime = (float)GetFloat("LapBestLapTime");
        data.AirTempC = (float)GetFloat("AirTemp");
        data.TrackTempC = (float)GetFloat("TrackTempCrew");
        data.Skies = GetInt("Skies");
        data.PitstopActive = GetBool("PitstopActive");
        data.PitRepairLeft = (float)GetFloat("PitRepairLeft");
        data.PitOptRepairLeft = (float)GetFloat("PitOptRepairLeft");
        data.FastRepairsUsed = GetInt("FastRepairUsed");
        data.FastRepairsAvailable = GetInt("FastRepairAvailable");
        data.IncidentCount = GetInt("PlayerCarMyIncidentCount");

        int carIdx = GetInt("PlayerCarIdx");

        string? yaml = _sdk.GetSessionInfo();
        if (!string.IsNullOrEmpty(yaml) && yaml != _lastYaml)
        {
            _lastYaml = yaml;
            RefreshStaticData(yaml, sessionNum);

            string? carName = IracingYaml.GetDriverValue(yaml, carIdx, "CarScreenNameShort");
            string? carCodeName = IracingYaml.GetDriverValue(yaml, carIdx, "CarPath");
            _carCodeName = carCodeName ?? string.Empty;

            Logger.Log($"Session info refreshed (SessionNum={sessionNum}, CarIdx={carIdx})");
            TrackCollector.Record(_trackName, _trackConfig, _trackCodeName);
            if (carName != null) CarCollector.Record(carName, carCodeName ?? string.Empty);
        }

        if (sessionNum != _lastSessionNum)
        {
            _sessionStartUtc = DateTime.UtcNow;
            _lastSessionNum = sessionNum;
        }

        data.TrackName = _trackName;
        data.TrackConfig = _trackConfig;
        data.TrackCodeName = _trackCodeName;
        data.SessionStartUtc = _sessionStartUtc;
        data.StrengthOfField = _strengthOfField;

        if (_lastYaml is not null)
        {
            data.SessionType = FormatSessionType(IracingYaml.GetSessionValue(_lastYaml, sessionNum, "SessionType"));
            data.CarName = IracingYaml.GetDriverValue(_lastYaml, carIdx, "CarScreenNameShort") ?? string.Empty;
            data.CarCodeName = _carCodeName;
            data.PlayerIRating = IracingYaml.GetDriverValue(_lastYaml, carIdx, "IRating") is { } iratingStr
                && int.TryParse(iratingStr, out int irating) ? irating : 0;
        }

        Logger.Log(FormatPollBlock(data, sessionFlags));

        return data;
    }

    private static string FormatPollBlock(SessionData d, int sessionFlags)
    {
        string lap = d.CurrentLap > 0
            ? (d.LapsRemain is > 0 and < 32767 ? $"{d.CurrentLap}/{d.CurrentLap + d.LapsRemain}" : $"{d.CurrentLap}")
            : "-";
        string timeRemain = d.TimeRemaining > 0 && d.TimeRemaining < 86400
            ? TimeSpan.FromSeconds(d.TimeRemaining).ToString(@"h\:mm\:ss")
            : "-";
        string flag = d.IsCheckered ? "Checkered" : d.IsCaution ? "Caution" : "Green";
        string pitGarage = d.OnPitRoad ? "Pit" : d.IsInGarage ? "Garage" : "-";

        return "Poll" + Environment.NewLine +
            $"  Connected     {d.IsConnected}   OnTrack={d.IsOnTrack}   Replay={d.IsReplay}" + Environment.NewLine +
            $"  Session       {(d.SessionType.Length > 0 ? d.SessionType : "-")}   Pos={(d.Position > 0 ? $"P{d.Position}" : "-")}   Lap={lap}   TimeLeft={timeRemain}" + Environment.NewLine +
            $"  Track         {d.TrackName}{(d.TrackConfig.Length > 0 ? $" / {d.TrackConfig}" : "")}   [{(d.TrackCodeName.Length > 0 ? d.TrackCodeName : "-")}]" + Environment.NewLine +
            $"  Car           {(d.CarName.Length > 0 ? d.CarName : "-")}   [{(d.CarCodeName.Length > 0 ? d.CarCodeName : "-")}]" + Environment.NewLine +
            $"  Speed/Fuel    {d.Speed * 3.6f:F0} km/h   {d.FuelLevel:F1}L ({d.FuelPercent * 100:F0}%)" + Environment.NewLine +
            $"  Flag/Pit      {flag} / {pitGarage}   (Flags=0x{sessionFlags:X4})   Service={d.PitstopActive}   Repair={d.PitRepairLeft:F0}s/{d.PitOptRepairLeft:F0}s   FR={d.FastRepairsUsed}/{d.FastRepairsAvailable}" + Environment.NewLine +
            $"  Class/Weather ClassPos={(d.ClassPosition > 0 ? $"P{d.ClassPosition}" : "-")}   Air={d.AirTempC:F0}C   Track={d.TrackTempC:F0}C   Skies={d.Skies}   Incidents={d.IncidentCount}";
    }

    private void RefreshStaticData(string yaml, int sessionNum)
    {
        string? shortName   = IracingYaml.GetValue(yaml, "TrackDisplayShortName");
        string? displayName = IracingYaml.GetValue(yaml, "TrackDisplayName");
        string? config      = IracingYaml.GetValue(yaml, "TrackConfigName");
        string? internalKey = IracingYaml.GetValue(yaml, "TrackName");  // e.g. "nurburgring combined"

        Logger.Log($"YAML raw — short='{shortName}' display='{displayName}' config='{config}' key='{internalKey}'");

        _trackCodeName = (internalKey != null && !internalKey.Contains(':')) ? internalKey : string.Empty;
        _trackConfig = (config != null && !config.Contains(':')) ? config : string.Empty;

        // TrackDisplayShortName can sometimes be the layout name (same as config) rather than the base
        // track name — particularly for multi-layout tracks like Nürburgring Combined.
        // Prefer DisplayName (the reliable full name), fall back to ShortName if it's distinct,
        // then fall back to capitalising the internal TrackName key.
        bool displayOk = displayName != null && !displayName.Contains(':') && displayName != _trackConfig;
        bool shortOk   = shortName   != null && !shortName.Contains(':')   && shortName   != _trackConfig;

        if (displayOk)
            _trackName = displayName!;
        else if (shortOk)
            _trackName = shortName!;
        else if (internalKey != null && !internalKey.Contains(':'))
            _trackName = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(internalKey.Replace('_', ' '));
        else
            _trackName = string.Empty;

        _strengthOfField = ComputeStrengthOfField(IracingYaml.GetCompetitorIRatings(yaml));
    }

    // iRacing's own SoF formula: the rating R for which 10^(-R/400) equals the average of
    // 10^(-iRating_i/400) across the field (an Elo-style power mean, not a plain average —
    // weaker drivers pull SoF down disproportionately, matching iRacing's published behaviour).
    // Sanity check: a field where every driver has the same rating R must reduce to SoF == R.
    private static int ComputeStrengthOfField(List<int> iratings)
    {
        if (iratings.Count == 0) return 0;
        double sum = 0;
        foreach (int ir in iratings)
            sum += Math.Pow(10, -ir / 400.0);
        return (int)Math.Round(-400 * Math.Log10(sum / iratings.Count));
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
