using System.Diagnostics;

namespace MacroHub.Core;

/// <summary>HID consumer usages that Windows acts on by itself, and the virtual keys with the same effect.</summary>
public static class NativeConsumer
{
    private static readonly Dictionary<int, ushort> UsageToVk = new()
    {
        [0xE9] = 0xAF, // Volume Increment → VK_VOLUME_UP
        [0xEA] = 0xAE, // Volume Decrement → VK_VOLUME_DOWN
        [0xE2] = 0xAD, // Mute → VK_VOLUME_MUTE
        [0xCD] = 0xB3, // Play/Pause → VK_MEDIA_PLAY_PAUSE
        [0xB5] = 0xB0, // Scan Next → VK_MEDIA_NEXT_TRACK
        [0xB6] = 0xB1, // Scan Previous → VK_MEDIA_PREV_TRACK
        [0xB7] = 0xB2, // Stop → VK_MEDIA_STOP
    };

    public static ushort? VkFor(int usage) => UsageToVk.TryGetValue(usage, out var vk) ? vk : null;

    /// <summary>Volume usages are the ones the knob-volume guard can undo.</summary>
    public static bool IsVolume(int usage) => usage is 0xE9 or 0xEA or 0xE2;

    /// <summary>True when <paramref name="action"/> is a plain tap of exactly the key the control's native usage already triggers.</summary>
    public static bool DuplicatesNativeEffect(ActionSpec action, IReadOnlyList<int>? usages)
    {
        if (usages is null || usages.Count == 0 || action is not KeysAction { Hold: false } k) return false;
        if (!KeyChord.TryParseSequence(k.Keys, out var chords, out _) || chords.Count != 1) return false;
        var c = chords[0];
        return c.Modifiers.Count == 0 && c.Keys.Count == 1 && usages.Any(u => VkFor(u) == c.Keys[0]);
    }
}

/// <summary>Monotonic high-resolution clock shared by the Hub (Environment.TickCount64 only ticks every ~15.6 ms).</summary>
public static class HubClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();

    /// <summary>Whole milliseconds since process start.</summary>
    public static long Ms => (Stopwatch.GetTimestamp() - Origin) * 1000 / Stopwatch.Frequency;

    /// <summary>Fractional milliseconds since process start (event timestamps, knob velocity).</summary>
    public static double MsPrecise => (Stopwatch.GetTimestamp() - Origin) * 1000.0 / Stopwatch.Frequency;
}
