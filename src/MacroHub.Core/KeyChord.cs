namespace MacroHub.Core;

/// <summary>A single chord: modifiers held while <see cref="Keys"/> are tapped in order (usually one key).</summary>
public sealed record KeyChord(IReadOnlyList<ushort> Modifiers, IReadOnlyList<ushort> Keys)
{
    /// <summary>Parse "Ctrl+Shift+S, Alt+P" into a list of chords.</summary>
    public static bool TryParseSequence(string text, out List<KeyChord> chords, out string error)
    {
        chords = [];
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "empty key chord"; return false; }
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParse(part, out var chord, out error)) return false;
            chords.Add(chord);
        }
        return chords.Count > 0;
    }

    public static bool TryParse(string text, out KeyChord chord, out string error)
    {
        chord = new KeyChord([], []);
        error = "";
        var mods = new List<ushort>();
        var keys = new List<ushort>();
        // "+" alone (or trailing "++") means the plus key.
        var tokens = SplitTokens(text);
        foreach (var raw in tokens)
        {
            if (!VirtualKeys.TryGet(raw, out ushort vk)) { error = $"unknown key '{raw}' in '{text}'"; return false; }
            if (VirtualKeys.IsModifier(vk) && keys.Count == 0 && raw != tokens[^1]) mods.Add(vk);
            else keys.Add(vk);
        }
        if (keys.Count == 0 && mods.Count == 0) { error = $"empty chord '{text}'"; return false; }
        chord = new KeyChord(mods, keys);
        return true;
    }

    private static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        var t = text.Trim();
        int start = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] != '+') continue;
            if (i == start) { tokens.Add("+"); start = i + 1; continue; } // literal plus
            tokens.Add(t[start..i].Trim());
            start = i + 1;
        }
        if (start < t.Length) tokens.Add(t[start..].Trim());
        return tokens.Where(s => s.Length > 0).ToList();
    }

    public override string ToString() =>
        string.Join("+", Modifiers.Concat(Keys).Select(VirtualKeys.NameOf));
}

/// <summary>Windows virtual-key names. Kept in Core (plain numbers) so parsing is unit-testable.</summary>
public static class VirtualKeys
{
    private static readonly Dictionary<string, ushort> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<ushort, string> ByCode = [];

    static VirtualKeys()
    {
        void Add(ushort vk, params string[] names)
        {
            foreach (var n in names) ByName[n] = vk;
            ByCode.TryAdd(vk, names[0]);
        }
        Add(0x10, "Shift"); Add(0x11, "Ctrl", "Control"); Add(0x12, "Alt", "Menu");
        Add(0x5B, "Win", "LWin", "Meta"); Add(0xA0, "LShift"); Add(0xA1, "RShift");
        Add(0xA2, "LCtrl"); Add(0xA3, "RCtrl"); Add(0xA4, "LAlt"); Add(0xA5, "RAlt");
        Add(0x08, "Backspace", "Back"); Add(0x09, "Tab"); Add(0x0D, "Enter", "Return");
        Add(0x13, "Pause"); Add(0x14, "CapsLock"); Add(0x1B, "Esc", "Escape"); Add(0x20, "Space");
        Add(0x21, "PageUp", "PgUp"); Add(0x22, "PageDown", "PgDn"); Add(0x23, "End"); Add(0x24, "Home");
        Add(0x25, "Left"); Add(0x26, "Up"); Add(0x27, "Right"); Add(0x28, "Down");
        Add(0x2C, "PrintScreen", "PrtSc"); Add(0x2D, "Insert", "Ins"); Add(0x2E, "Delete", "Del");
        for (int d = 0; d <= 9; d++) Add((ushort)(0x30 + d), d.ToString());
        for (char c = 'A'; c <= 'Z'; c++) Add(c, c.ToString());
        Add(0x5D, "Apps", "ContextMenu");
        for (int d = 0; d <= 9; d++) Add((ushort)(0x60 + d), $"Num{d}", $"Numpad{d}");
        Add(0x6A, "NumMultiply", "Num*"); Add(0x6B, "NumAdd", "Num+"); Add(0x6D, "NumSubtract", "Num-");
        Add(0x6E, "NumDecimal", "Num."); Add(0x6F, "NumDivide", "Num/");
        for (int f = 1; f <= 24; f++) Add((ushort)(0x6F + f), $"F{f}");
        Add(0x90, "NumLock"); Add(0x91, "ScrollLock");
        Add(0xA6, "BrowserBack"); Add(0xA7, "BrowserForward");
        Add(0xAD, "VolumeMute", "Mute"); Add(0xAE, "VolumeDown"); Add(0xAF, "VolumeUp");
        Add(0xB0, "MediaNext"); Add(0xB1, "MediaPrev"); Add(0xB2, "MediaStop"); Add(0xB3, "MediaPlayPause", "PlayPause");
        Add(0xBA, ";", "Semicolon"); Add(0xBB, "=", "Equals", "Plus"); Add(0xBC, ",", "Comma");
        Add(0xBD, "-", "Minus"); Add(0xBE, ".", "Period"); Add(0xBF, "/", "Slash"); Add(0xC0, "`", "Backquote", "Tilde");
        Add(0xDB, "[", "LBracket"); Add(0xDC, "\\", "Backslash"); Add(0xDD, "]", "RBracket"); Add(0xDE, "'", "Quote");
        ByName["+"] = 0xBB;
    }

    public static bool TryGet(string name, out ushort vk) => ByName.TryGetValue(name.Trim(), out vk);

    public static string NameOf(ushort vk) => ByCode.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}";

    public static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when injected with SendInput.</summary>
    public static bool IsExtended(ushort vk) => vk is (>= 0x21 and <= 0x28) or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D
        or 0x6F or 0x90 or 0xA3 or 0xA5 or (>= 0xA6 and <= 0xB7);

    public static IEnumerable<string> AllNames => ByName.Keys.OrderBy(n => n);
}
