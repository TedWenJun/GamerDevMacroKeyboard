using System.Globalization;

namespace MacroHub.Core;

/// <summary>
/// One backlight state of the pad: which of the firmware's effects runs, how bright and how fast it is, which way
/// it travels and in what colour. The device stores a seven colour palette per effect, but a single colour written
/// to every slot covers every effect, so the Hub exposes just one.
/// </summary>
public sealed class LightingSpec
{
    public const int ModeCount = 9;
    /// <summary>Mode 9 switches the backlight off and takes no parameters.</summary>
    public const int ModeOff = 9;

    /// <summary>Effect, 1-9, as numbered by the firmware (see docs/device-protocol.md).</summary>
    public int Mode { get; set; } = 1;
    /// <summary>1 (dimmest) to 6 (brightest).</summary>
    public int Brightness { get; set; } = 5;
    /// <summary>0 (slowest) to 5 (fastest); only the animated effects use it.</summary>
    public int Speed { get; set; } = 3;
    /// <summary>0 or 1; only the flowing effects use it.</summary>
    public int Direction { get; set; }
    public string Color { get; set; } = "#ffffff";

    /// <summary>Effect names as the pad's own tool shows them, index 0 = mode 1.</summary>
    public static readonly string[] ModeNames =
    [
        "Solid", "Flowing", "Marquee", "Breathing", "Cycling breath", "Tetris", "Neon", "Rainbow flow", "Off",
    ];

    /// <summary>Brightness steps the firmware actually has (measured: six, and byte 0 is the brightest).</summary>
    public const int BrightnessLevels = 6;

    /// <summary>
    /// The firmware stores brightness as a plain 0-5 with 0 the brightest, so the Hub's 1-6 - where 6 is
    /// brightest, the way a slider reads - is inverted here.
    /// </summary>
    public static byte BrightnessByte(int level) => (byte)(BrightnessLevels - Math.Clamp(level, 1, BrightnessLevels));

    public static string ModeName(int mode) =>
        mode >= 1 && mode <= ModeNames.Length ? ModeNames[mode - 1] : mode.ToString(CultureInfo.InvariantCulture);

    /// <summary>The colour as the device wants it: plain R, G, B bytes.</summary>
    public (byte R, byte G, byte B) Rgb()
    {
        var hex = (Color ?? "").TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            return (0xFF, 0xFF, 0xFF);
        }
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public bool SameAs(LightingSpec other) =>
        Mode == other.Mode && Brightness == other.Brightness && Speed == other.Speed &&
        Direction == other.Direction && string.Equals(Color, other.Color, StringComparison.OrdinalIgnoreCase);

    public LightingSpec Clone() => new()
    {
        Mode = Mode,
        Brightness = Brightness,
        Speed = Speed,
        Direction = Direction,
        Color = Color,
    };

    /// <summary>Clamp anything a client may have sent into the ranges the firmware accepts.</summary>
    public LightingSpec Sanitize()
    {
        Mode = Math.Clamp(Mode, 1, ModeCount);
        Brightness = Math.Clamp(Brightness, 1, BrightnessLevels);
        Speed = Math.Clamp(Speed, 0, 5);
        Direction = Math.Clamp(Direction, 0, 1);
        var (r, g, b) = Rgb();
        Color = $"#{r:x2}{g:x2}{b:x2}";
        return this;
    }
}

/// <summary>Hub-wide backlight settings.</summary>
public sealed class LightingConfig
{
    /// <summary>The Hub only writes to the pad's backlight when this is on; off leaves the device alone.</summary>
    public bool Enabled { get; set; }
    /// <summary>Applied at startup and whenever the active layer has no lighting of its own.</summary>
    public LightingSpec Default { get; set; } = new();
    /// <summary>Re-apply on every layer change, so a layer's own lighting shows which layer is active.</summary>
    public bool FollowLayer { get; set; } = true;
}
