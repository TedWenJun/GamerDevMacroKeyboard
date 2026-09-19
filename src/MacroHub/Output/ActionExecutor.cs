using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroHub.Core;
using MacroHub.Input;
using MacroHub.Native;

namespace MacroHub.Output;

/// <summary>
/// Executes actions on a dedicated worker so the keyboard hook never waits on SendInput or process launch.
/// Everything injected carries <see cref="InputThread.InjectMarker"/>.
/// </summary>
public sealed class ActionExecutor : IDisposable
{
    private readonly BlockingCollection<(ActionSpec action, ControlPhase phase, string holdKey)> _queue = new();
    private readonly Thread _worker;
    private readonly ILogger _log;
    private readonly Router _router;
    /// <summary>Hold actions currently pressed, keyed by control id, so Up releases exactly what Down pressed.</summary>
    private readonly Dictionary<string, KeyChord> _heldChords = [];

    public event Action<string>? Error;

    public ActionExecutor(ILogger log, Router router)
    {
        _log = log;
        _router = router;
        _worker = new Thread(Work) { IsBackground = true, Name = "MacroHub Actions" };
        _worker.Start();
    }

    /// <param name="holdKey">Identity used to pair Down/Up of hold actions (usually the control id).</param>
    public void Enqueue(ActionSpec action, ControlPhase phase, string holdKey) => _queue.Add((action, phase, holdKey));

    /// <summary>True when the action does something on control Up.</summary>
    public static bool WantsUp(ActionSpec a) => a is KeysAction { Hold: true } or MouseAction { Hold: true };

    private void Work()
    {
        foreach (var (action, phase, holdKey) in _queue.GetConsumingEnumerable())
        {
            try { Execute(action, phase, holdKey); }
            catch (Exception e)
            {
                _log.LogError(e, "action {Action} failed", action.Describe());
                Error?.Invoke($"{action.Describe()}: {e.Message}");
            }
        }
    }

    private void Execute(ActionSpec action, ControlPhase phase, string holdKey)
    {
        switch (action)
        {
            case KeysAction { Hold: true } k:
                if (!KeyChord.TryParse(k.Keys.Split(',')[0], out var chord, out var err)) throw new InvalidOperationException(err);
                if (phase == ControlPhase.Down)
                {
                    lock (_heldChords) _heldChords[holdKey] = chord;
                    Send([.. chord.Modifiers.Select(vk => Key(vk, false)), .. chord.Keys.Select(vk => Key(vk, false))]);
                }
                else
                {
                    KeyChord? held;
                    lock (_heldChords) _heldChords.Remove(holdKey, out held);
                    held ??= chord;
                    Send([.. held.Keys.Reverse().Select(vk => Key(vk, true)), .. held.Modifiers.Reverse().Select(vk => Key(vk, true))]);
                }
                break;
            case KeysAction k when phase == ControlPhase.Down:
                if (!KeyChord.TryParseSequence(k.Keys, out var chords, out var err2)) throw new InvalidOperationException(err2);
                foreach (var c in chords) TapChord(c);
                break;
            case TextAction t when phase == ControlPhase.Down:
                TypeText(t.Text);
                break;
            case MacroAction m when phase == ControlPhase.Down:
                foreach (var step in m.Steps)
                {
                    if (step.Keys is { Length: > 0 } && KeyChord.TryParseSequence(step.Keys, out var sc, out _)) foreach (var c in sc) TapChord(c);
                    if (step.Text is { Length: > 0 }) TypeText(step.Text);
                    if (step.DelayMs > 0) Thread.Sleep(Math.Min(step.DelayMs, 10_000));
                }
                break;
            case RunAction r when phase == ControlPhase.Down:
                Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(r.Path), r.Args ?? "") { UseShellExecute = true });
                break;
            case MouseAction mouse:
                ExecuteMouse(mouse, phase, holdKey);
                break;
            case LayerAction l when phase == ControlPhase.Down:
                _router.SetLayer(l);
                break;
        }
    }

    private void ExecuteMouse(MouseAction m, ControlPhase phase, string holdKey)
    {
        if (m.Button is { } b)
        {
            var (down, up, data) = b.ToLowerInvariant() switch
            {
                "right" => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP, 0u),
                "middle" => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP, 0u),
                "x1" => (User32.MOUSEEVENTF_XDOWN, User32.MOUSEEVENTF_XUP, 1u),
                "x2" => (User32.MOUSEEVENTF_XDOWN, User32.MOUSEEVENTF_XUP, 2u),
                _ => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP, 0u),
            };
            if (!m.Hold && phase == ControlPhase.Down) Send([Mouse(down, data), Mouse(up, data)]);
            else if (m.Hold) Send([Mouse(phase == ControlPhase.Down ? down : up, data)]);
            return;
        }
        if (phase != ControlPhase.Down) return;
        if (m.Wheel != 0) Send([Mouse(User32.MOUSEEVENTF_WHEEL, unchecked((uint)(m.Wheel * 120)))]);
        if (m.HWheel != 0) Send([Mouse(User32.MOUSEEVENTF_HWHEEL, unchecked((uint)(m.HWheel * 120)))]);
    }

    private static void TapChord(KeyChord c)
    {
        var inputs = new List<User32.INPUT>();
        inputs.AddRange(c.Modifiers.Select(vk => Key(vk, false)));
        foreach (var vk in c.Keys) { inputs.Add(Key(vk, false)); inputs.Add(Key(vk, true)); }
        inputs.AddRange(c.Modifiers.Reverse().Select(vk => Key(vk, true)));
        Send([.. inputs]);
    }

    private static void TypeText(string text)
    {
        var inputs = new List<User32.INPUT>();
        foreach (char ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); continue; }
            inputs.Add(Unicode(ch, false));
            inputs.Add(Unicode(ch, true));
        }
        Send([.. inputs]);
    }

    private static User32.INPUT Key(ushort vk, bool up)
    {
        uint flags = up ? User32.KEYEVENTF_KEYUP : 0;
        if (VirtualKeys.IsExtended(vk)) flags |= User32.KEYEVENTF_EXTENDEDKEY;
        return new User32.INPUT
        {
            type = User32.INPUT_KEYBOARD,
            u = new User32.INPUTUNION { ki = new User32.KEYBDINPUT { wVk = vk, wScan = (ushort)User32.MapVirtualKey(vk, 0), dwFlags = flags, dwExtraInfo = InputThread.InjectMarker } },
        };
    }

    private static User32.INPUT Unicode(char ch, bool up) => new()
    {
        type = User32.INPUT_KEYBOARD,
        u = new User32.INPUTUNION { ki = new User32.KEYBDINPUT { wScan = ch, dwFlags = User32.KEYEVENTF_UNICODE | (up ? User32.KEYEVENTF_KEYUP : 0), dwExtraInfo = InputThread.InjectMarker } },
    };

    private static User32.INPUT Mouse(uint flags, uint data) => new()
    {
        type = User32.INPUT_MOUSE,
        u = new User32.INPUTUNION { mi = new User32.MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = InputThread.InjectMarker } },
    };

    private static void Send(User32.INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<User32.INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput injected {sent}/{inputs.Length} (error {Marshal.GetLastWin32Error()}; target may be elevated — run MacroHub as administrator)");
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(2000);
    }
}
