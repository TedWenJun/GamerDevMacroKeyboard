using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroHub.Core;

/// <summary>
/// Three-tier configuration:
///   Device   – hardware signatures → logical control ids (K1, KNOB_CW, JOY_UP …)
///   System   – functions (semantic commands with a default action) and layers mapping controls → functions
///   App      – per-application profiles overriding a function's action or forwarding it to the app client
/// </summary>
public sealed class HubConfig
{
    public int Version { get; set; } = 1;
    public DeviceMap Device { get; set; } = new();
    public List<FunctionDef> Functions { get; set; } = [];
    public List<LayerDef> Layers { get; set; } = [];
    public List<AppProfile> Apps { get; set; } = [];
    /// <summary>What to do with pad input that resolves to nothing: "block" or "passthrough".</summary>
    public string Unbound { get; set; } = "passthrough";
    /// <summary>
    /// How pad keystrokes are told apart from other keyboards:
    ///   correlate – match hook events against physical HID reports read from the pad (default, stock firmware)
    ///   codes     – any key listed in a control's key signatures is treated as pad input (unique codes like F13-F24,
    ///               or when no physical HID channel is available)
    ///   off       – never block; actions still fire from physical reports
    /// </summary>
    public string Suppression { get; set; } = "correlate";
    /// <summary>
    /// The W909 knob always changes the Windows master volume (consumer usage handled by the OS, not blockable).
    /// When a knob event is used for something else, restore the volume right after the OS changed it.
    /// </summary>
    public bool KnobVolumeGuard { get; set; } = true;

    /// <summary>Backlight settings; the Hub leaves the pad's lighting alone unless this is enabled.</summary>
    public LightingConfig Lighting { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions CompactJsonOptions = new(JsonOptions) { WriteIndented = false };

    public static HubConfig Load(string path) =>
        (JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(path), JsonOptions) ?? new HubConfig()).Normalize();

    /// <summary>Upgrade legacy fields in place (idempotent). Returns this.</summary>
    public HubConfig Normalize()
    {
        // Configs written before the 2.4G receiver and Bluetooth were known only list the W909 cable; the other
        // modes expose the same collections and reports.
        if (Device.Match.Any(m => m.Equals(PadTransport.W909Match[0], StringComparison.OrdinalIgnoreCase)))
            foreach (var m in PadTransport.W909Match)
                if (!Device.Match.Any(x => x.Equals(m, StringComparison.OrdinalIgnoreCase))) Device.Match.Add(m);

        foreach (var a in Apps)
        {
            if (a.ForwardAll == true && a.Forward == AppProfile.ForwardOff) a.Forward = AppProfile.ForwardFunctions;
            a.ForwardAll = null;
        }
        return this;
    }

    private static readonly object SaveGate = new();

    /// <summary>Atomic save (write a unique temp file, then replace). Serialized so concurrent saves cannot collide.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        lock (SaveGate)
        {
            var tmp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    public HubConfig Clone() => JsonSerializer.Deserialize<HubConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Returns a list of human readable problems (empty when valid).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        var controlIds = Device.Controls.Select(c => c.Id).ToHashSet();
        var fnIds = new HashSet<string>();
        foreach (var f in Functions)
            if (!fnIds.Add(f.Id)) errors.Add($"duplicate function id '{f.Id}'");
        if (Layers.Count == 0) errors.Add("at least one layer is required");
        var layerIds = new HashSet<string>();
        foreach (var l in Layers)
        {
            if (!layerIds.Add(l.Id)) errors.Add($"duplicate layer id '{l.Id}'");
            if (l.Fallback is not ("inherit" or "passthrough" or "block")) errors.Add($"layer '{l.Id}': fallback must be inherit, passthrough or block");
        }
        foreach (var c in Device.Controls)
            if (c.Input is { } ci) errors.AddRange(ci.Validate($"control '{c.Id}' input"));
        foreach (var l in Layers)
            foreach (var (control, li) in l.Input ?? [])
            {
                if (!controlIds.Contains(control)) errors.Add($"layer '{l.Id}': input for unknown control '{control}'");
                errors.AddRange(li.Validate($"layer '{l.Id}' input '{control}'"));
            }
        foreach (var a in Apps)
        {
            if (a.Layer is { } al && !layerIds.Contains(al)) errors.Add($"app '{a.Id}': unknown layer '{al}'");
            if (a.Forward is not (AppProfile.ForwardOff or AppProfile.ForwardFunctions or AppProfile.ForwardControls))
                errors.Add($"app '{a.Id}': forward must be off, functions or controls");
        }
        foreach (var l in Layers)
            foreach (var (control, fn) in l.Map)
            {
                if (!controlIds.Contains(control)) errors.Add($"layer '{l.Id}': unknown control '{control}'");
                if (!fnIds.Contains(fn)) errors.Add($"layer '{l.Id}': unknown function '{fn}'");
            }
        foreach (var a in Apps)
            foreach (var fn in a.Overrides.Keys)
                if (!fnIds.Contains(fn)) errors.Add($"app '{a.Id}': unknown function '{fn}'");
        // Physical signatures must be unique; key signatures may be shared (the physical signal disambiguates).
        var seen = new Dictionary<string, string>();
        foreach (var c in Device.Controls)
            foreach (var s in c.Signatures.Where(s => !s.StartsWith("key:")))
                if (!seen.TryAdd(s, c.Id)) errors.Add($"signature '{s}' used by both '{seen[s]}' and '{c.Id}'");
        foreach (var f in Functions)
            if (f.Action is KeysAction k && !KeyChord.TryParseSequence(k.Keys, out _, out var err))
                errors.Add($"function '{f.Id}': {err}");
        foreach (var a in Apps)
            foreach (var (fn, act) in a.Overrides)
                if (act is KeysAction k && !KeyChord.TryParseSequence(k.Keys, out _, out var err))
                    errors.Add($"app '{a.Id}' function '{fn}': {err}");
        return errors;
    }
}

public sealed class DeviceMap
{
    public string Name { get; set; } = "";
    /// <summary>Substrings matched (case-insensitive) against HID device paths, e.g. "VID_B6A4&amp;PID_4100".</summary>
    public List<string> Match { get; set; } = [];
    public byte VendorReportId { get; set; } = 5;
    public List<ControlDef> Controls { get; set; } = [];
}

public sealed class ControlDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>key, knob, joystick — used by the web UI.</summary>
    public string Kind { get; set; } = "key";
    /// <summary>Sub-part of a knob/joystick sharing one rect: cw, ccw, press, up, down, left, right.</summary>
    public string? Part { get; set; }
    /// <summary>Layout rectangle in abstract units for the web UI: x, y, w, h.</summary>
    public double[] Rect { get; set; } = [0, 0, 1, 1];
    /// <summary>Input shaping for all layers (detents per trigger, throttle, hold-to-repeat); null = defaults.</summary>
    public InputSettings? Input { get; set; }
    /// <summary>Signatures (see <see cref="Signal.Signature"/>) that identify this control. The first vendor
    /// signature is the physical identity; key signatures are what the stock firmware types.</summary>
    public List<string> Signatures { get; set; } = [];
}

public sealed class FunctionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public ActionSpec Action { get; set; } = new PassthroughAction();
}

public sealed class LayerDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#f5c400";
    /// <summary>
    /// Controls not mapped in this layer: "inherit" (use the base layer, i.e. the first layer), "passthrough"
    /// (original key goes through) or "block". For the base layer itself "inherit" falls back to <see cref="HubConfig.Unbound"/>.
    /// </summary>
    public string Fallback { get; set; } = "inherit";
    /// <summary>Per-layer input shaping overrides: control id → settings (replaces the control's own settings).</summary>
    public Dictionary<string, InputSettings>? Input { get; set; }
    /// <summary>Backlight applied while this layer is active; null keeps the Hub-wide default.</summary>
    public LightingSpec? Lighting { get; set; }
    /// <summary>control id → function id</summary>
    public Dictionary<string, string> Map { get; set; } = [];
}

public sealed class AppProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Executable names (case-insensitive, '*' wildcard), e.g. "UnrealEditor.exe", "*-Win64-Shipping.exe".</summary>
    public List<string> Processes { get; set; } = [];
    /// <summary>Optional layer to activate automatically while this app is in the foreground.</summary>
    public string? Layer { get; set; }
    public const string ForwardOff = "off", ForwardFunctions = "functions", ForwardControls = "controls";

    /// <summary>
    /// What a connected client of this app receives while the app is in the foreground:
    ///   off       - nothing; functions run as local actions
    ///   functions - function events resolved through the Hub layers (client handles them, local action skipped)
    ///   controls  - raw control events; the Hub skips its layers and the client decides by its own context
    ///               (e.g. the UE plugin maps the knob to the time slider in Sequencer). With no client connected
    ///               the Hub falls back to its layers and local actions.
    /// </summary>
    public string Forward { get; set; } = ForwardOff;

    /// <summary>Legacy (v1) switch, read only for migration: true means <see cref="Forward"/> = functions.</summary>
    public bool? ForwardAll { get; set; }
    /// <summary>function id → action used while this app is in the foreground</summary>
    public Dictionary<string, ActionSpec> Overrides { get; set; } = [];
}
