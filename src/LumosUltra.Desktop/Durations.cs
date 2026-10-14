namespace LumosUltra.Desktop;

/// <summary>How long something took, for people: "4:07" under an hour, "1:04:07" from an hour up.</summary>
public static class Durations
{
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}
