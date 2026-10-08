namespace PcAiDashboard;

public static class ScreenSaverPolicy
{
    public static bool ShowsDashboard(string displayId, IReadOnlyCollection<string>? selected)
        => selected == null || selected.Contains(displayId, StringComparer.OrdinalIgnoreCase);

    public static bool ShouldStart(bool enabled, int minutes, uint now, uint lastInput, long sinceStop)
        => enabled && unchecked(now - lastInput) >= Math.Clamp(minutes, 1, 240) * 60_000L
            && sinceStop >= Math.Clamp(minutes, 1, 240) * 60_000L;
}
