namespace iRPC;

public class StatsData
{
    public ulong TotalSeconds { get; set; }
    public ulong TotalLaps { get; set; }
    public ulong TotalDistanceM { get; set; }
    public ulong TotalIncidents { get; set; }
    public Dictionary<string, ulong> SecondsBySessionType { get; set; } = new();
    public Dictionary<string, ulong> SecondsByCar { get; set; } = new();
    public Dictionary<string, ulong> SecondsByTrack { get; set; } = new();
    public Dictionary<string, ulong> LapsByTrack { get; set; } = new();

    public StatsData Clone() => new()
    {
        TotalSeconds    = TotalSeconds,
        TotalLaps       = TotalLaps,
        TotalDistanceM  = TotalDistanceM,
        TotalIncidents  = TotalIncidents,
        SecondsBySessionType = new(SecondsBySessionType),
        SecondsByCar         = new(SecondsByCar),
        SecondsByTrack       = new(SecondsByTrack),
        LapsByTrack          = new(LapsByTrack),
    };
}
