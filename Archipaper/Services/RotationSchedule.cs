namespace Archipaper.Services;

public sealed class RotationSchedule
{
    public int IntervalMinutes { get; set; } = 60;
    public Dictionary<string, MonitorSchedule> Monitors { get; set; } = new();

    public void Configure(int minutes, DateTimeOffset now)
    {
        minutes = Math.Clamp(minutes, 5, 1440);
        Monitors ??= new();
        foreach (var state in Monitors.Values)
        {
            if (state.LastSuccess > now)
            {
                state.LastSuccess = now;
                state.DueAt = now.AddMinutes(minutes);
            }
            else if (IntervalMinutes != minutes && state.Failures == 0)
                state.DueAt = (state.LastSuccess ?? now).AddMinutes(minutes);
            if (state.DueAt > now.AddMinutes(Math.Max(minutes, 15))) state.DueAt = now.AddMinutes(minutes);
        }
        IntervalMinutes = minutes;
    }
    public MonitorSchedule Ensure(string id, DateTimeOffset now)
    {
        if (!Monitors.TryGetValue(id, out var state)) Monitors[id] = state = new() { DueAt = now.AddMinutes(IntervalMinutes) };
        return state;
    }
    public bool IsDue(string id, DateTimeOffset now) => Ensure(id, now).DueAt <= now;
    public void Succeeded(string id, DateTimeOffset now)
    {
        var state = Ensure(id, now);
        state.LastSuccess = now; state.Failures = 0; state.DueAt = now.AddMinutes(IntervalMinutes);
    }
    public void Failed(string id, DateTimeOffset now)
    {
        var state = Ensure(id, now);
        state.Failures = Math.Clamp(state.Failures + 1, 1, 5);
        state.DueAt = now.AddMinutes(Math.Min(15, Math.Pow(2, state.Failures - 1)));
    }
}
public sealed class MonitorSchedule
{
    public DateTimeOffset? LastSuccess { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int Failures { get; set; }
}
