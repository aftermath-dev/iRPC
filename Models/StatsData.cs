namespace iRPC;

public class StatsData
{
    public long TotalSeconds { get; set; }
    public Dictionary<string, long> SecondsBySessionType { get; set; } = new();
    public Dictionary<string, long> SecondsByCar { get; set; } = new();
    public Dictionary<string, long> SecondsByTrack { get; set; } = new();

    public StatsData Clone() => new()
    {
        TotalSeconds = TotalSeconds,
        SecondsBySessionType = new(SecondsBySessionType),
        SecondsByCar = new(SecondsByCar),
        SecondsByTrack = new(SecondsByTrack),
    };
}
