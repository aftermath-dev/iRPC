namespace iRPC;

public class SessionData
{
    public bool IsConnected { get; set; }
    public bool IsOnTrack { get; set; }
    public bool IsReplay { get; set; }
    public string SessionType { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public string TrackConfig { get; set; } = string.Empty;
    public string CarName { get; set; } = string.Empty;
    public int Position { get; set; }
    public int CurrentLap { get; set; }
    public int LapsRemain { get; set; }   // 32767 = unlimited
    public double TimeRemaining { get; set; }  // seconds; very large = unlimited
    public bool IsCaution { get; set; }
    public bool IsCheckered { get; set; }
    public DateTime? SessionStartUtc { get; set; }
}
