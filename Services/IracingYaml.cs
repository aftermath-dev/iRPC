namespace iRPC;

// Minimal parser for iRacing's YAML-like session info format.
// Uses IndexOf-based scanning to avoid allocating substrings or split arrays on every call.
// iRacing's YAML uses indented keys (e.g. " TrackName:" under WeekendInfo, "   SessionType:"
// inside list blocks) — FindValue handles any indent by checking that only spaces precede
// the key on its line, rather than requiring an exact "\nKey:" match.
public static class IracingYaml
{
    public static string? GetValue(string yaml, string key)
        => FindValue(yaml, 0, yaml.Length, key);

    // Returns the value of 'key' from the Sessions block whose SessionNum matches.
    public static string? GetSessionValue(string yaml, int sessionNum, string key)
    {
        string sessionNumStr = sessionNum.ToString();
        string marker = "- SessionNum:";
        int searchFrom = 0;

        while (true)
        {
            int blockStart = yaml.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (blockStart < 0) return null;

            // Read the SessionNum value from this block
            int numStart = blockStart + marker.Length;
            while (numStart < yaml.Length && yaml[numStart] == ' ') numStart++;
            int numEnd = yaml.IndexOf('\n', numStart);
            if (numEnd < 0) numEnd = yaml.Length;
            string numVal = yaml.Substring(numStart, numEnd - numStart).TrimEnd();

            // Find end of this block (next "- SessionNum:" or end of Sessions section)
            int nextBlock = yaml.IndexOf(marker, numEnd, StringComparison.Ordinal);
            int blockEnd = nextBlock > 0 ? nextBlock : yaml.Length;

            if (numVal == sessionNumStr)
                return FindValue(yaml, blockStart, blockEnd, key);

            searchFrom = numEnd;
        }
    }

    // Returns value of 'key' from the Drivers list entry whose CarIdx matches.
    public static string? GetDriverValue(string yaml, int carIdx, string key)
    {
        string carIdxStr = carIdx.ToString();
        string marker = "- CarIdx:";
        int searchFrom = yaml.IndexOf("Drivers:", StringComparison.Ordinal);
        if (searchFrom < 0) return null;

        while (true)
        {
            int blockStart = yaml.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (blockStart < 0) return null;

            int numStart = blockStart + marker.Length;
            while (numStart < yaml.Length && yaml[numStart] == ' ') numStart++;
            int numEnd = yaml.IndexOf('\n', numStart);
            if (numEnd < 0) numEnd = yaml.Length;
            string numVal = yaml.Substring(numStart, numEnd - numStart).TrimEnd();

            int nextBlock = yaml.IndexOf(marker, numEnd, StringComparison.Ordinal);
            int blockEnd = nextBlock > 0 ? nextBlock : yaml.Length;

            if (numVal == carIdxStr)
                return FindValue(yaml, blockStart, blockEnd, key);

            searchFrom = numEnd;
        }
    }

    // Returns the IRating of every real competitor (excludes pace car and spectators).
    public static List<int> GetCompetitorIRatings(string yaml)
    {
        var result = new List<int>();
        string marker = "- CarIdx:";
        int searchFrom = yaml.IndexOf("Drivers:", StringComparison.Ordinal);
        if (searchFrom < 0) return result;

        while (true)
        {
            int blockStart = yaml.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (blockStart < 0) break;

            int nextBlock = yaml.IndexOf(marker, blockStart + marker.Length, StringComparison.Ordinal);
            int blockEnd = nextBlock > 0 ? nextBlock : yaml.Length;

            if (FindValue(yaml, blockStart, blockEnd, "CarIsPaceCar") == "1"
             || FindValue(yaml, blockStart, blockEnd, "IsSpectator") == "1")
            {
                searchFrom = blockStart + marker.Length;
                continue;
            }

            string? iratingStr = FindValue(yaml, blockStart, blockEnd, "IRating");
            if (iratingStr != null && int.TryParse(iratingStr, out int irating) && irating > 0)
                result.Add(irating);

            searchFrom = blockStart + marker.Length;
        }

        return result;
    }

    // Finds 'key:' within yaml[start..end) where the key appears at the start of a line
    // (preceded only by spaces — handles any indentation level).
    // Skips occurrences where the key is a substring of a longer identifier.
    private static string? FindValue(string yaml, int start, int end, string key)
    {
        string needle = key + ":";
        int from = start;

        while (from < end)
        {
            int idx = yaml.IndexOf(needle, from, end - from, StringComparison.Ordinal);
            if (idx < 0) return null;

            // Walk backward past spaces; must reach a newline (or start of string/range).
            int p = idx - 1;
            while (p >= 0 && yaml[p] == ' ') p--;
            bool atLineStart = p < 0 || yaml[p] == '\n';

            if (atLineStart)
            {
                int vs = idx + needle.Length;
                while (vs < end && yaml[vs] == ' ') vs++;
                int ve = yaml.IndexOf('\n', vs, Math.Max(0, end - vs));
                if (ve < 0 || ve > end) ve = end;
                string val = yaml.Substring(vs, ve - vs).TrimEnd();
                return val.Length == 0 ? null : val;
            }

            from = idx + needle.Length;
        }

        return null;
    }
}
