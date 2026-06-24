using System.Text.RegularExpressions;

namespace iRPC;

// Minimal parser for iRacing's YAML-like session info format.
public static class IracingYaml
{
    // Returns first occurrence of "key: value" anywhere in the yaml string.
    public static string? GetValue(string yaml, string key)
    {
        var m = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}:\s*(.*?)\s*$", RegexOptions.Multiline);
        if (!m.Success) return null;
        string val = m.Groups[1].Value.Trim();
        return val.Length == 0 ? null : val;
    }

    // Returns the value of 'key' from the Sessions block whose SessionNum matches.
    public static string? GetSessionValue(string yaml, int sessionNum, string key)
    {
        // Split on session-list items: lines starting with "- SessionNum:"
        string[] blocks = Regex.Split(yaml, @"(?=^\s*-\s+SessionNum:\s*\d)", RegexOptions.Multiline);
        foreach (string block in blocks)
        {
            var numMatch = Regex.Match(block, @"-\s+SessionNum:\s*(\d+)");
            if (numMatch.Success && int.Parse(numMatch.Groups[1].Value) == sessionNum)
                return GetValue(block, key);
        }
        return null;
    }

    // Returns value of 'key' from the Drivers list entry whose CarIdx matches.
    public static string? GetDriverValue(string yaml, int carIdx, string key)
    {
        foreach (string block in GetDriverBlocks(yaml))
        {
            var idxMatch = Regex.Match(block, @"-\s+CarIdx:\s*(\d+)");
            if (idxMatch.Success && int.Parse(idxMatch.Groups[1].Value) == carIdx)
                return GetValue(block, key);
        }
        return null;
    }

    // Returns the IRating of every real, currently-entered competitor (excludes the pace car
    // and spectator slots, which carry meaningless/zero IRating values that would skew SoF).
    public static List<int> GetCompetitorIRatings(string yaml)
    {
        var result = new List<int>();
        foreach (string block in GetDriverBlocks(yaml))
        {
            if (GetValue(block, "CarIsPaceCar") == "1") continue;
            if (GetValue(block, "IsSpectator") == "1") continue;
            if (GetValue(block, "IRating") is { } iratingStr && int.TryParse(iratingStr, out int irating) && irating > 0)
                result.Add(irating);
        }
        return result;
    }

    private static IEnumerable<string> GetDriverBlocks(string yaml)
    {
        // Find the Drivers: list — handles both LF and CRLF
        var driversMatch = Regex.Match(yaml, @"^\s*Drivers:\s*$", RegexOptions.Multiline);
        if (!driversMatch.Success) return [];
        string driversList = yaml[driversMatch.Index..];

        // Split on driver entries: "- CarIdx:"
        return Regex.Split(driversList, @"(?=\s*-\s+CarIdx:)");
    }
}
