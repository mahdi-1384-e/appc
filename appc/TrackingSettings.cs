namespace appc;

public static class TrackingSettings
{
    const string KEnabled = "cfg_enabled";
    const string KInterval = "interval_sec";
    const string KStart = "win_start"; // "HH:mm"
    const string KEnd = "win_end";     // "HH:mm"
    const string KUploadMin = "upload_every_min";
    const string KUpdatedAtUtc = "cfg_updated_at_utc"; // string

    public static bool Enabled
    {
        get => Preferences.Get(KEnabled, true);
        set => Preferences.Set(KEnabled, value);
    }

    public static int IntervalSeconds
    {
        get => Preferences.Get(KInterval, 10);
        set => Preferences.Set(KInterval, value);
    }

    public static string WindowStart
    {
        get => Preferences.Get(KStart, "00:00");
        set => Preferences.Set(KStart, value);
    }

    public static string WindowEnd
    {
        get => Preferences.Get(KEnd, "23:59");
        set => Preferences.Set(KEnd, value);
    }

    public static int UploadEveryMinutes
    {
        get => Preferences.Get(KUploadMin, 5);
        set => Preferences.Set(KUploadMin, value);
    }

    public static string UpdatedAtUtcRaw
    {
        get => Preferences.Get(KUpdatedAtUtc, "");
        set => Preferences.Set(KUpdatedAtUtc, value ?? "");
    }

    public static bool IsNowInWindow()
    {
        var now = DateTime.Now.TimeOfDay;

        if (!TimeSpan.TryParse(WindowStart, out var s)) s = TimeSpan.Zero;
        if (!TimeSpan.TryParse(WindowEnd, out var e)) e = new TimeSpan(23, 59, 0);

        if (s <= e) return now >= s && now <= e;   // normal
        return now >= s || now <= e;              // crosses midnight
    }
}