namespace iRPC;

public class SessionData
{
    public bool IsConnected { get; set; }
    public bool IsOnTrack { get; set; }
    public bool IsReplay { get; set; }
    public string SessionType { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public string TrackConfig { get; set; } = string.Empty;
    public string TrackCodeName { get; set; } = string.Empty;
    public string CarName { get; set; } = string.Empty;
    public string CarCodeName { get; set; } = string.Empty;
    public int Position { get; set; }
    public int CurrentLap { get; set; }
    public int LapsRemain { get; set; }    // 32767 = unlimited
    public double TimeRemaining { get; set; }
    public bool IsCaution { get; set; }
    public bool IsCheckered { get; set; }
    public DateTime? SessionStartUtc { get; set; }
    public float Speed { get; set; }        // m/s
    public float FuelLevel { get; set; }    // liters
    public float FuelPercent { get; set; }  // 0–1
    public bool OnPitRoad { get; set; }
    public float LastLapTime { get; set; }  // seconds; -1 = no valid lap yet
    public float BestLapTime { get; set; }  // seconds; -1 = no valid lap yet
    public int StrengthOfField { get; set; }  // 0 = not yet computed / no competitors
    public int PlayerIRating { get; set; }    // 0 = unranked / not yet read
    public int IRatingAvg5 { get; set; }       // average of player's last 5 race-end iRatings; 0 = no history yet
    public int IRatingAvg10 { get; set; }      // average of player's last 10 race-end iRatings; 0 = no history yet
    public int IRatingAvgCustom { get; set; }  // average over AppSettings.IRatingAvgCustomWindow races; 0 = no history yet
    public int ClassPosition { get; set; }     // position within player's car class (multiclass races); 0 outside race
    public float AirTempC { get; set; }
    public float TrackTempC { get; set; }
    public int Skies { get; set; }             // 0=Clear, 1=Partly Cloudy, 2=Mostly Cloudy, 3=Overcast
    public bool PitstopActive { get; set; }
    public float PitRepairLeft { get; set; }      // seconds of mandatory damage repair remaining
    public float PitOptRepairLeft { get; set; }    // seconds of optional/cosmetic repair remaining
    public int FastRepairsUsed { get; set; }
    public int FastRepairsAvailable { get; set; }
    public int IncidentCount { get; set; }
    public bool IsInGarage => IsConnected && !IsOnTrack && !OnPitRoad;
}
