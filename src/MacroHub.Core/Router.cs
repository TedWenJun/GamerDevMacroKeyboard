namespace MacroHub.Core;

/// <summary>Result of routing one control event through layer → function → app profile.</summary>
public sealed record RouteResult(
    string Control,
    ControlPhase Phase,
    string Layer,
    string? Function,
    string? App,
    ActionSpec Action,
    bool Forward,
    bool RawControls = false)
{
    /// <summary>True when the pad's original input must be suppressed.</summary>
    public bool Block => Action is not PassthroughAction;
}

/// <summary>
/// Pure routing logic, no OS dependencies. Thread-safe for reads; call <see cref="Apply"/> to swap config.
/// </summary>
public sealed class Router
{
    private volatile Snapshot _s;
    private string _manualLayer;
    private readonly object _gate = new();

    private sealed record Snapshot(
        HubConfig Config,
        Dictionary<string, string> SignatureToControl,
        Dictionary<ushort, List<string>> VkToControls,
        Dictionary<string, List<ushort>> ControlToVks,
        Dictionary<string, List<int>> ControlConsumerUsages,
        Dictionary<string, FunctionDef> Functions,
        Dictionary<string, LayerDef> Layers);

    public Router(HubConfig config)
    {
        config.Normalize();
        _s = Build(config);
        _manualLayer = config.Layers.FirstOrDefault()?.Id ?? "base";
    }

    public HubConfig Config => _s.Config;

    /// <summary>The layer chosen by the user (knob/layer actions). An app profile may override it while focused.</summary>
    public string ManualLayer { get { lock (_gate) return _manualLayer; } }

    public event Action<string>? LayerChanged;

    public void Apply(HubConfig config)
    {
        config.Normalize();
        var s = Build(config);
        lock (_gate)
        {
            _s = s;
            if (!s.Layers.ContainsKey(_manualLayer)) _manualLayer = config.Layers.FirstOrDefault()?.Id ?? "base";
        }
    }

    private static Snapshot Build(HubConfig c)
    {
        var sig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vkTo = new Dictionary<ushort, List<string>>();
        var toVk = new Dictionary<string, List<ushort>>();
        var consumer = new Dictionary<string, List<int>>();
        foreach (var ctl in c.Device.Controls)
            foreach (var s in ctl.Signatures)
            {
                sig.TryAdd(s, ctl.Id);
                if (s.StartsWith("key:", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(s.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var vk))
                {
                    (vkTo.TryGetValue(vk, out var l) ? l : vkTo[vk] = []).Add(ctl.Id);
                    (toVk.TryGetValue(ctl.Id, out var k) ? k : toVk[ctl.Id] = []).Add(vk);
                }
                else if (s.StartsWith("consumer:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(s.AsSpan(9), System.Globalization.NumberStyles.HexNumber, null, out var usage))
                    (consumer.TryGetValue(ctl.Id, out var u) ? u : consumer[ctl.Id] = []).Add(usage);
            }
        return new Snapshot(c, sig, vkTo, toVk, consumer,
            c.Functions.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First()),
            c.Layers.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First()));
    }

    public string? ControlForSignature(string signature) =>
        _s.SignatureToControl.TryGetValue(signature, out var id) ? id : null;

    /// <summary>Controls whose stock firmware output includes this virtual key (empty when the key never comes from the pad).</summary>
    public IReadOnlyList<string> ControlsForVk(ushort vk) =>
        _s.VkToControls.TryGetValue(vk, out var l) ? l : [];

    public IReadOnlyList<ushort> VksForControl(string control) =>
        _s.ControlToVks.TryGetValue(control, out var l) ? l : [];

    /// <summary>Effective input shaping for a control in a layer: layer override, else the control's own, else defaults.</summary>
    public InputSettings ResolveInput(string control, string layerId)
    {
        var s = _s;
        if (s.Layers.TryGetValue(layerId, out var layer) && layer.Input is { } li && li.TryGetValue(control, out var ov)) return ov;
        return s.Config.Device.Controls.FirstOrDefault(c => c.Id == control)?.Input ?? InputSettings.Default;
    }

    /// <summary>Controls drawn as one physical part (knob CW/CCW/press) share a group; rotating back resets progress.</summary>
    public string? GroupOf(string control)
    {
        var def = _s.Config.Device.Controls.FirstOrDefault(c => c.Id == control);
        return def is null || def.Kind == "key" ? null : def.Kind + ":" + string.Join(",", def.Rect);
    }

    /// <summary>Consumer usages (e.g. volume) the control produces natively; the OS acts on them and they cannot be blocked.</summary>
    public IReadOnlyList<int> ConsumerUsagesForControl(string control) =>
        _s.ControlConsumerUsages.TryGetValue(control, out var l) ? l : [];

    public AppProfile? MatchApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;
        return _s.Config.Apps.FirstOrDefault(a => a.Processes.Any(p => GlobMatch(p, processName)));
    }

    internal static bool GlobMatch(string pattern, string text)
    {
        if (!pattern.Contains('*')) return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);
        var parts = pattern.Split('*');
        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length == 0) continue;
            int idx = text.IndexOf(p, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || (i == 0 && idx != 0)) return false;
            pos = idx + p.Length;
        }
        return parts[^1].Length == 0 || text.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
    }

    public string EffectiveLayer(string? processName)
    {
        var app = MatchApp(processName);
        if (app?.Layer is { } l && _s.Layers.ContainsKey(l)) return l;
        return ManualLayer;
    }

    public RouteResult Route(string control, ControlPhase phase, string? foregroundProcess)
    {
        var s = _s;
        var app = MatchApp(foregroundProcess);
        string layerId = EffectiveLayer(foregroundProcess);
        string? fn = null;
        string fallback = s.Config.Unbound;
        s.Layers.TryGetValue(layerId, out var layer);
        if (layer is not null && layer.Map.TryGetValue(control, out var f)) fn = f;
        else if (layer is { Fallback: "passthrough" or "block" }) fallback = layer.Fallback;
        else if (s.Config.Layers.Count > 0 && s.Config.Layers[0].Map.TryGetValue(control, out var baseFn)) fn = baseFn; // inherit from base layer

        if (fn is null || !s.Functions.TryGetValue(fn, out var def))
        {
            ActionSpec unbound = fallback == "block" ? new NoneAction() : new PassthroughAction();
            return new RouteResult(control, phase, layerId, fn, app?.Id, unbound, false);
        }

        var action = app is not null && app.Overrides.TryGetValue(fn, out var ov) ? ov : def.Action;
        // The knob already changes the volume natively; injecting the same media key again would double the step.
        if (NativeConsumer.DuplicatesNativeEffect(action, s.ControlConsumerUsages.GetValueOrDefault(control)))
            action = new PassthroughAction();
        bool forward = action is ForwardAction || app?.Forward == AppProfile.ForwardFunctions;
        return new RouteResult(control, phase, layerId, fn, app?.Id, action, forward);
    }

    public void SetLayer(LayerAction op)
    {
        string changed;
        lock (_gate)
        {
            var layers = _s.Config.Layers;
            if (layers.Count == 0) return;
            int idx = Math.Max(0, layers.FindIndex(l => l.Id == _manualLayer));
            _manualLayer = op.Op switch
            {
                "next" => layers[(idx + 1) % layers.Count].Id,
                "prev" => layers[(idx - 1 + layers.Count) % layers.Count].Id,
                "set" when op.Layer is not null && _s.Layers.ContainsKey(op.Layer) => op.Layer,
                _ => _manualLayer,
            };
            changed = _manualLayer;
        }
        LayerChanged?.Invoke(changed);
    }
}
