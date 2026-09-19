namespace MacroHub.Core;

/// <summary>
/// How raw control activity turns into action triggers. Applies to local actions and function forwarding;
/// raw control forwarding (UE plugin) always receives every event.
/// Set on a control (all layers) and optionally overridden per layer.
/// </summary>
public sealed record InputSettings
{
    /// <summary>Rotary controls: trigger once every N detents in the same direction.</summary>
    public int StepDetents { get; init; } = 1;
    /// <summary>Minimum time between two triggers of this control; faster triggers are dropped.</summary>
    public int MinIntervalMs { get; init; }
    /// <summary>Partial detent progress is discarded after this much idle time.</summary>
    public int ResetMs { get; init; } = 800;
    /// <summary>Keys / joystick: repeat the action while the control is held.</summary>
    public bool Repeat { get; init; }
    public int RepeatDelayMs { get; init; } = 400;
    public int RepeatIntervalMs { get; init; } = 100;

    public static readonly InputSettings Default = new();

    public IEnumerable<string> Validate(string where)
    {
        if (StepDetents is < 1 or > 50) yield return $"{where}: stepDetents must be 1-50";
        if (MinIntervalMs is < 0 or > 5000) yield return $"{where}: minIntervalMs must be 0-5000";
        if (ResetMs is < 50 or > 10000) yield return $"{where}: resetMs must be 50-10000";
        if (RepeatDelayMs is < 50 or > 5000) yield return $"{where}: repeatDelayMs must be 50-5000";
        if (RepeatIntervalMs is < 16 or > 5000) yield return $"{where}: repeatIntervalMs must be 16-5000";
    }
}

/// <summary>
/// Decides which control Downs trigger an action (detent accumulation + throttling). Thread-safe.
/// Controls sharing a <c>group</c> (e.g. knob CW and CCW) reset each other's progress: turning back starts over.
/// </summary>
public sealed class ControlStepper
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (int count, long lastDetent, long lastTrigger)> _state = [];
    private readonly Dictionary<string, string> _lastInGroup = [];

    public readonly record struct Result(bool Trigger, int Progress, int Needed);

    public Result OnDown(string control, string? group, InputSettings s, long now)
    {
        lock (_gate)
        {
            var (count, lastDetent, lastTrigger) = _state.TryGetValue(control, out var st) ? st : (0, long.MinValue / 2, long.MinValue / 2);
            if (group is not null)
            {
                if (_lastInGroup.TryGetValue(group, out var prev) && prev != control) count = 0; // direction changed
                _lastInGroup[group] = control;
            }
            if (now - lastDetent > s.ResetMs) count = 0;
            count++;
            lastDetent = now;
            bool trigger = false;
            int needed = Math.Max(1, s.StepDetents);
            if (count >= needed)
            {
                count = 0;
                if (now - lastTrigger >= s.MinIntervalMs)
                {
                    trigger = true;
                    lastTrigger = now;
                }
            }
            _state[control] = (count, lastDetent, lastTrigger);
            return new Result(trigger, trigger ? needed : count, needed);
        }
    }

    public void Reset()
    {
        lock (_gate) { _state.Clear(); _lastInGroup.Clear(); }
    }
}
