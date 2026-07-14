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
    private string _carName = string.Empty;
    private string _carNumber = string.Empty;
    private string _carClass = string.Empty;
    private string _licenseString = string.Empty;
    private string _seriesName = string.Empty;
    private string _sessionType = string.Empty;
    private int _playerIRating;
    private float _playerSRating;
    private string _playerName = string.Empty;
    private int _strengthOfField;
    private int _totalDrivers;
    private int _incidentLimit;
    private int _lastResolvedCarIdx = -1;
    private int _lastSessionNum = -1;
    private DateTime? _sessionStartUtc;
    private DateTime? _stintStartUtc;
    private bool _lastOnPitRoad;
    private bool _lastIsOnTrack;

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
            _carName = _carNumber = _carClass = _licenseString = _seriesName = _sessionType = string.Empty;
            _playerIRating = 0;
            _playerSRating = 0;
            _totalDrivers = 0;
            _lastResolvedCarIdx = -1;
            _lastSessionNum = -1;
            _sessionStartUtc = null;
            _stintStartUtc = null;
            _lastOnPitRoad = false;
            _lastIsOnTrack = false;
            return new SessionData();
        }
    }

    private SessionData PollInternal()
    {
        var data = new SessionData();

        if (!_sdk.IsConnected())
        {
            _lastSessionNum = -1;
            _sessionStartUtc = null;
            _stintStartUtc = null;
            _lastOnPitRoad = false;
            _lastIsOnTrack = false;
            return data;
        }

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
        data.Flag = (sessionFlags & 0x0001) != 0 ? FlagState.Checkered
                  : (sessionFlags & 0x10000) != 0 ? FlagState.Black
                  : (sessionFlags & 0x0010) != 0 ? FlagState.Red
                  : (sessionFlags & 0x0002) != 0 ? FlagState.White
                  : (sessionFlags & 0xC108) != 0 ? FlagState.Caution   // Caution | CautionWaving | Yellow | YellowWaving
                  : (sessionFlags & 0x0004) != 0 ? FlagState.Green
                  : FlagState.None;
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
        data.CurrentLapTime = (float)GetFloat("LapCurrentLapTime");
        data.Gear = GetInt("Gear");
        data.RPM = (float)GetFloat("RPM");
        data.FuelUsePerHour = (float)GetFloat("FuelUsePerHour");
        data.SessionTime = GetFloat("SessionTime");
        data.WindSpeedMS = (float)GetFloat("WindVel");
        data.WindDirRad = (float)GetFloat("WindDir");
        data.Humidity = (float)GetFloat("Humidity");
        data.WeatherDeclaredWet = GetBool("WeatherDeclaredWet");
        data.SimTimeOfDay = (float)GetFloat("SessionTimeOfDay");

        int carIdx = GetInt("PlayerCarIdx");
        data.TireCompound = GetStringAtIndex("CarIdxTireCompound", carIdx);
        data.LapDelta = (float)GetFloat("LapDeltaToBestLap");

        // Stint timer: reset when entering pits or going off track; start when joining track or exiting pits
        bool enteringTrack = data.IsOnTrack && !_lastIsOnTrack;
        bool exitingPits   = _lastOnPitRoad && !data.OnPitRoad && data.IsOnTrack;
        bool enteringPits  = !_lastOnPitRoad && data.OnPitRoad;
        if (!data.IsOnTrack || enteringPits)
            _stintStartUtc = null;
        else if (enteringTrack || exitingPits)
            _stintStartUtc = DateTime.UtcNow;
        data.StintStartUtc = _stintStartUtc;
        _lastOnPitRoad = data.OnPitRoad;
        _lastIsOnTrack = data.IsOnTrack;

        // Gap to leader, car directly ahead, and laps down
        if (data.Position > 1)
        {
            int[] carPositions = GetIntArray("CarIdxPosition");
            float[] carF2Times = GetFloatArray("CarIdxF2Time");
            int[] carLaps = GetIntArray("CarIdxLap");
            int targetPos = data.Position - 1;
            float playerF2 = carIdx < carF2Times.Length ? carF2Times[carIdx] : -1f;
            data.GapToLeader = playerF2;
            bool foundAhead = false;
            for (int i = 0; i < carPositions.Length; i++)
            {
                if (!foundAhead && carPositions[i] == targetPos && i < carF2Times.Length && carF2Times[i] >= 0)
                {
                    if (playerF2 >= 0) data.GapAhead = playerF2 - carF2Times[i];
                    foundAhead = true;
                }
                if (carPositions[i] == 1 && i < carLaps.Length && carLaps[i] >= 0)
                    data.LapsDown = Math.Max(0, carLaps[i] - data.CurrentLap);
            }
        }

        string? yaml = _sdk.GetSessionInfo();
        if (!string.IsNullOrEmpty(yaml) && yaml != _lastYaml)
        {
            _lastYaml = yaml;
            RefreshStaticData(yaml, sessionNum);
            _seriesName  = IracingYaml.GetValue(yaml, "SeriesName") ?? string.Empty;
            _sessionType = FormatSessionType(IracingYaml.GetSessionValue(yaml, sessionNum, "SessionType"));
            if (Logger.Enabled) Logger.Log($"Session info refreshed (SessionNum={sessionNum}, CarIdx={carIdx})");
            TrackCollector.Record(_trackName, _trackConfig, _trackCodeName);
        }

        // Driver fields are keyed on carIdx (PlayerCarIdx telemetry var). Re-read whenever
        // carIdx changes so that if PlayerCarIdx stabilises after the first YAML read (which
        // happened in Test Drive before 4c0db5a broke it), the correct car data is picked up.
        if (carIdx != _lastResolvedCarIdx && _lastYaml is { } cachedYaml)
        {
            // Test Drive (offline) can report a different PlayerCarIdx in telemetry than in
            // the YAML's DriverCarIdx. Try the telemetry value first; fall back to the YAML.
            int resolvedIdx = carIdx;
            if (IracingYaml.GetDriverValue(cachedYaml, carIdx, "CarPath") is null
                && IracingYaml.GetValue(cachedYaml, "DriverCarIdx") is { } yamlIdxStr
                && int.TryParse(yamlIdxStr, out int yamlIdx) && yamlIdx != carIdx)
            {
                resolvedIdx = yamlIdx;
            }

            _carName       = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "CarScreenNameShort")
                             ?? IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "CarScreenName")
                             ?? string.Empty;
            _carCodeName   = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "CarPath") ?? string.Empty;
            _playerIRating = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "IRating") is { } irStr
                && int.TryParse(irStr, out int ir) ? ir : 0;
            _carNumber     = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "CarNumber")?.Trim().Trim('"') ?? string.Empty;
            _carClass      = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "CarClassShortName") ?? string.Empty;
            _licenseString = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "LicString") ?? string.Empty;
            _playerSRating = ParseSRating(_licenseString);
            _playerName    = IracingYaml.GetDriverValue(cachedYaml, resolvedIdx, "UserName") ?? string.Empty;
            _lastResolvedCarIdx = carIdx;
            if (_carName.Length > 0) CarCollector.Record(_carName, _carCodeName);
        }

        if (sessionNum != _lastSessionNum)
        {
            _sessionStartUtc = DateTime.UtcNow;
            _lastSessionNum = sessionNum;
            if (_lastYaml is not null)
                _sessionType = FormatSessionType(IracingYaml.GetSessionValue(_lastYaml, sessionNum, "SessionType"));
        }

        data.TrackName = _trackName;
        data.TrackConfig = _trackConfig;
        data.TrackCodeName = _trackCodeName;
        data.SessionStartUtc = _sessionStartUtc;
        data.StrengthOfField = _strengthOfField;
        data.TotalDrivers = _totalDrivers;
        data.IncidentLimit = _incidentLimit;

        data.SessionType   = _sessionType;
        data.CarName       = _carName;
        data.CarCodeName   = _carCodeName;
        data.PlayerIRating = _playerIRating;
        data.PlayerSRating = _playerSRating;
        data.CarNumber     = _carNumber;
        data.CarClass      = _carClass;
        data.LicenseString = _licenseString;
        data.SeriesName    = _seriesName;
        data.PlayerName    = _playerName;

        if (Logger.Enabled) Logger.Log(FormatPollBlock(data, sessionFlags));

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

        var competitors = IracingYaml.GetCompetitorIRatings(yaml);
        _strengthOfField = ComputeStrengthOfField(competitors);
        _totalDrivers = competitors.Count;

        string? limitStr = IracingYaml.GetValue(yaml, "IncidentLimit");
        _incidentLimit = limitStr != null && int.TryParse(limitStr, out int lim) ? lim : 0;
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

    private static float ParseSRating(string licStr)
    {
        if (string.IsNullOrWhiteSpace(licStr)) return 0;
        foreach (var part in licStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(part, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float sr) && sr > 0)
                return sr;
        }
        return 0;
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

    private int[] GetIntArray(string name)
    {
        try { return _sdk.GetData(name) is int[] arr ? arr : []; }
        catch { return []; }
    }

    private float[] GetFloatArray(string name)
    {
        try { return _sdk.GetData(name) is float[] arr ? arr : []; }
        catch { return []; }
    }

    private string GetStringAtIndex(string name, int index)
    {
        try
        {
            return _sdk.GetData(name) switch
            {
                string[] arr => index >= 0 && index < arr.Length ? arr[index] : string.Empty,
                string s     => s,
                _            => string.Empty
            };
        }
        catch { return string.Empty; }
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
