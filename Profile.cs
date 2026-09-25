using System.Diagnostics;

namespace DefconSaver;

/// <summary>Stage timings for the /g capture mode. Off, and free, in normal use.</summary>
public static class Profile
{
    public static bool On;

    private static readonly Dictionary<string, double> Acc = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static double Now => On ? Clock.Elapsed.TotalMilliseconds : 0.0;

    public static double Mark(string name, double since)
    {
        if (!On) return 0.0;
        double now = Clock.Elapsed.TotalMilliseconds;
        Acc[name] = Acc.TryGetValue(name, out double v) ? v + (now - since) : now - since;
        return now;
    }

    public static string Dump()
    {
        if (!On) return "";
        var parts = Acc.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value:0.0}");
        string s = string.Join("  ", parts);
        Acc.Clear();
        return s;
    }
}
