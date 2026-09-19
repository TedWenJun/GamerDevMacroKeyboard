namespace MacroHub.Core;

/// <summary>
/// Decides whether a keyboard event seen by the low-level hook originated from the macro pad, and whether to block it.
///
/// Measured on the W909 (stock firmware, USB): the vendor bitmap report / consumer report read directly from the
/// pad's HID collections arrives ~1-2 ms BEFORE the WH_KEYBOARD_LL callback, while Raw Input (WM_INPUT) arrives
/// AFTER it — so Raw Input cannot be used to decide inside the hook, but the direct HID reads can.
///
/// Flow: physical Down for control C → <see cref="Expect"/> arms C (block or pass):
///         - its known key signatures, valid for <see cref="Window"/>
///         - a wildcard for ANY key, valid for <see cref="WildcardWindow"/>. The firmware has modes (knob press
///           switches keys 1-6 from Num1-6 to A-F), so the typed code is not a reliable identity; timing is.
///       hook key-down matching an armed expectation → verdict for the pad, vk remembered as held.
///       auto-repeat downs and the key-up of a held vk → same verdict.
/// All methods are thread-safe. Time is supplied by the caller (milliseconds, monotonic).
/// </summary>
public sealed class SuppressionCorrelator
{
    private const ushort AnyKey = 0;

    private readonly object _gate = new();
    private readonly List<(long t, ushort vk, string control, bool block)> _armed = [];
    private readonly Dictionary<ushort, (string control, bool block)> _held = [];
    private readonly List<(long t, ushort vk)> _passed = [];

    /// <summary>How long an expectation for a known key signature stays valid.</summary>
    public long Window { get; set; } = 80;

    /// <summary>How long the any-key expectation stays valid. Short, so a simultaneous main-keyboard key is unlikely to be taken.</summary>
    public long WildcardWindow { get; set; } = 25;

    /// <summary>How far back a physical Down looks for an already-passed key of the control to count it as leaked.</summary>
    public long LeakWindow { get; set; } = 60;

    /// <summary>How far back a physical Down looks for an already-passed key of ANY code to count it as leaked.</summary>
    public long WildcardLeakWindow { get; set; } = 12;

    /// <summary>Raised after <see cref="Expect"/> so a hook waiting on a candidate key can re-classify.</summary>
    public event Action? Armed;

    public enum Verdict
    {
        /// <summary>Not from the pad — let it through.</summary>
        NotPad,
        /// <summary>From the pad and the route blocks the original key.</summary>
        PadBlock,
        /// <summary>From the pad but the route lets the original key through.</summary>
        PadPass,
        /// <summary>The pad can produce this key but no physical signal has been seen (yet).</summary>
        Unknown,
    }

    /// <summary>
    /// Called when a physical control goes down.
    /// Returns the number of keys that already went through unblocked shortly before (a "leak": the physical report
    /// arrived later than the hook was willing to wait).
    /// </summary>
    public int Expect(string control, IEnumerable<ushort> vks, bool block, long now, bool wildcard = true)
    {
        int leaked = 0;
        lock (_gate)
        {
            Prune(now);
            foreach (var vk in vks)
            {
                int i = _passed.FindIndex(p => p.vk == vk && now - p.t <= LeakWindow);
                if (i >= 0)
                {
                    // the key-down already reached the system; don't arm, or we would swallow an unrelated later press
                    _passed.RemoveAt(i);
                    if (block) leaked++;
                    continue;
                }
                _armed.Add((now, vk, control, block));
            }
            if (wildcard)
            {
                int i = _passed.FindIndex(p => now - p.t <= WildcardLeakWindow);
                if (i >= 0)
                {
                    _passed.RemoveAt(i);
                    if (block) leaked++;
                }
                else _armed.Add((now, AnyKey, control, block));
            }
        }
        Armed?.Invoke();
        return leaked;
    }

    /// <summary>
    /// Classify a hardware (non-injected) hook event. <see cref="Verdict.Unknown"/> is only returned for
    /// <paramref name="candidate"/> keys (codes the pad is known to type); the caller may wait briefly for
    /// <see cref="Armed"/> and ask again.
    /// </summary>
    public Verdict Classify(ushort vk, bool keyUp, bool candidate, long now, out string? control)
    {
        control = null;
        lock (_gate)
        {
            if (keyUp)
            {
                if (_held.Remove(vk, out var h)) { control = h.control; return h.block ? Verdict.PadBlock : Verdict.PadPass; }
                return Verdict.NotPad;
            }
            if (_held.TryGetValue(vk, out var held)) { control = held.control; return held.block ? Verdict.PadBlock : Verdict.PadPass; } // auto-repeat
            Prune(now);
            int i = _armed.FindIndex(a => a.vk == vk);
            if (i >= 0)
            {
                var a = _armed[i];
                // one physical press accounts for one key-down per known vk
                _armed.RemoveAll(x => x.control == a.control && x.vk == vk);
                return Hold(vk, a.control, a.block, out control);
            }
            i = _armed.FindIndex(a => a.vk == AnyKey && now - a.t <= WildcardWindow);
            if (i >= 0)
            {
                // wildcard stays armed for its (short) window: firmware macros may type several keys per press
                var a = _armed[i];
                return Hold(vk, a.control, a.block, out control);
            }
            return candidate ? Verdict.Unknown : Verdict.NotPad;
        }
    }

    private Verdict Hold(ushort vk, string ctl, bool block, out string? control)
    {
        _held[vk] = (ctl, block);
        control = ctl;
        return block ? Verdict.PadBlock : Verdict.PadPass;
    }

    /// <summary>A hardware key-down went through unblocked (used for leak accounting).</summary>
    public void NotePassed(ushort vk, long now)
    {
        lock (_gate)
        {
            _passed.RemoveAll(p => now - p.t > LeakWindow);
            if (_passed.Count > 64) _passed.RemoveAt(0);
            _passed.Add((now, vk));
        }
    }

    /// <summary>Physical release: drop expectations that were never matched by a key-down.</summary>
    public void PhysicalUp(string control)
    {
        lock (_gate) _armed.RemoveAll(a => a.control == control && a.vk != AnyKey);
    }

    public void Reset()
    {
        lock (_gate) { _armed.Clear(); _held.Clear(); _passed.Clear(); }
    }

    private void Prune(long now) => _armed.RemoveAll(a => now - a.t > (a.vk == AnyKey ? WildcardWindow : Window));
}
