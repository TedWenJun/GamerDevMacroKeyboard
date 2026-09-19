using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MacroHub.Audio;
using MacroHub.Clients;
using MacroHub.Core;
using MacroHub.Input;
using MacroHub.Output;

namespace MacroHub;

/// <summary>Fan-out of JSON events to WebSocket subscribers (web UI and WebSocket app clients).</summary>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subs = new();

    public (Guid id, ChannelReader<string> reader) Subscribe()
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subs[id] = ch;
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch)) ch.Writer.TryComplete();
    }

    public void Publish(object evt)
    {
        var json = JsonSerializer.Serialize(evt, HubConfig.CompactJsonOptions);
        foreach (var ch in _subs.Values) ch.Writer.TryWrite(json);
    }

    public bool SendTo(Guid id, object evt)
    {
        if (!_subs.TryGetValue(id, out var ch)) return false;
        return ch.Writer.TryWrite(evt as string ?? JsonSerializer.Serialize(evt, HubConfig.CompactJsonOptions));
    }
}

public sealed class HubEngine : IDisposable
{
    /// <summary>Application protocol version spoken by this Hub (see README "应用客户端协议").</summary>
    public const int ProtocolVersion = 2;

    private readonly ILogger<HubEngine> _log;
    private readonly string _configPath;
    private readonly SuppressionCorrelator _correlator = new();
    private readonly ManualResetEventSlim _armedSignal = new();
    private readonly ConcurrentDictionary<string, RouteResult> _downRoutes = new();
    /// <summary>Clients that received a control's Down, so its Up goes to the same clients even if focus moved.</summary>
    private readonly ConcurrentDictionary<string, List<AppClient>> _downTargets = new();
    private readonly HashSet<ushort> _codesHeld = [];
    private readonly ControlStepper _stepper = new();
    /// <summary>Controls whose last Down did not trigger (detent accumulation / throttle), so their Up is ignored too.</summary>
    private readonly ConcurrentDictionary<string, bool> _gatedDowns = new();
    private readonly ConcurrentDictionary<string, Timer> _repeaters = new();
    private readonly object _learnGate = new();
    private LearnSession? _learn;

    public Router Router { get; }
    public EventBus Bus { get; } = new();
    public ClientRegistry Clients { get; } = new();
    public PadHidReader Hid { get; }
    public InputThread Input { get; }
    public ActionExecutor Executor { get; }
    public VolumeGuard Volume { get; }
    public PadLighting Lighting { get; }
    public PipeServer? Pipe { get; set; }
    public string ConfigPath => _configPath;
    public string Version { get; set; } = "0.0.0";

    /// <summary>Suppression statistics since start (or the last reset).</summary>
    public sealed class Stats
    {
        public int PhysicalDowns;
        public int Suppressed;
        public int Leaked;
        public int WaitedForReport;
        public long MaxWaitMs;
    }
    public Stats Counters { get; private set; } = new();
    public void ResetStats() => Counters = new Stats();

    /// <summary>Backlight of the active layer, falling back to the Hub default.</summary>
    public LightingSpec EffectiveLighting()
    {
        var config = Router.Config;
        if (config.Lighting.FollowLayer)
        {
            var layerId = Router.EffectiveLayer(Input?.Foreground.Process);
            var layer = config.Layers.FirstOrDefault(l => l.Id == layerId);
            if (layer?.Lighting is { } own) return own;
        }
        return config.Lighting.Default;
    }

    /// <summary>
    /// Push the current layer's backlight to the pad. Writing three feature reports takes tens of milliseconds,
    /// so it never runs on the input path.
    /// </summary>
    public void ApplyLighting(bool force = false)
    {
        if (!Router.Config.Lighting.Enabled) return;
        var spec = EffectiveLighting().Clone().Sanitize();
        Task.Run(() =>
        {
            try { Lighting.Apply(spec, force); }
            catch (Exception e) { _log.LogWarning(e, "lighting apply failed"); }
        });
    }

    /// <summary>Last battery reading; null while the pad is away or has not answered yet.</summary>
    public PadBattery? Battery { get; private set; }
    private Timer? _batteryTimer;
    private const int BatteryPollMs = 30_000;
    private long _batteryNudgedAt = long.MinValue / 2; // halved so "now - it" cannot overflow

    /// <summary>
    /// A 2.4G pad that has gone to sleep does not answer the status poll, so the first input after it wakes asks
    /// again straight away instead of waiting up to 30 s. At most once every 5 s.
    /// </summary>
    private void NudgeBattery(long now)
    {
        if (Battery is not null || now - Interlocked.Read(ref _batteryNudgedAt) < 5_000) return;
        Interlocked.Exchange(ref _batteryNudgedAt, now);
        _batteryTimer?.Change(200, BatteryPollMs);
    }

    /// <summary>Read the battery over the feature channel (tens of milliseconds, never on the input path).</summary>
    private void PollBattery()
    {
        try
        {
            var reading = Hid.Connected ? Lighting.QueryBattery() : null;
            // A missed reply keeps the last reading while the pad is still attached.
            if (reading is null && Hid.Connected) return;
            if (reading == Battery) return;
            Battery = reading;
            if (reading is not null)
                _log.LogInformation("battery: {Percent}%{Charging}", reading.Percent, reading.Charging ? " (charging)" : "");
            Bus.Publish(new { type = "battery", battery = BatteryMessage() });
        }
        catch (Exception e) { _log.LogDebug(e, "battery poll failed"); }
    }

    private object? BatteryMessage() => Battery is { } b ? new { percent = b.Percent, charging = b.Charging } : null;

    /// <summary>How long the hook waits for the physical report of a key the pad can produce.</summary>
    public int CorrelateWaitMs { get; set; } = 8;

    private sealed class LearnSession
    {
        public required string Control { get; init; }
        public DateTime Expires { get; set; }
        public HashSet<string> Physical { get; } = [];
        public HashSet<string> Keys { get; } = [];
        public long LastPhysical { get; set; } = long.MinValue;
        public int Held { get; set; }
        public Timer? Finish { get; set; }
    }

    public HubEngine(ILogger<HubEngine> log, ILoggerFactory lf, string configPath)
    {
        _log = log;
        _configPath = configPath;
        var config = LoadOrSeed(configPath);
        Router = new Router(config);
        Router.LayerChanged += l => Bus.Publish(new { type = "layer", manualLayer = l, layer = Router.EffectiveLayer(Input?.Foreground.Process) });
        _correlator.Armed += () => _armedSignal.Set();

        Executor = new ActionExecutor(lf.CreateLogger<ActionExecutor>(), Router);
        Executor.Error += msg => Bus.Publish(new { type = "error", message = msg });

        Input = new InputThread(lf.CreateLogger<InputThread>()) { KeyFilter = FilterKey };
        Input.ForegroundChanged += fg => Bus.Publish(new { type = "foreground", fg.Process, fg.Pid, fg.Title, layer = Router.EffectiveLayer(fg.Process), app = Router.MatchApp(fg.Process)?.Id });

        Hid = new PadHidReader(lf.CreateLogger<PadHidReader>(), () => Router.Config.Device);
        Hid.SignalReceived += OnPhysicalSignal;
        Hid.ConnectionChanged += c => Bus.Publish(new { type = "device", connected = c, transport = Hid.Transport, collections = Hid.Collections });

        Volume = new VolumeGuard(lf.CreateLogger<VolumeGuard>());

        Lighting = new PadLighting(lf.CreateLogger<PadLighting>(), () => Router.Config.Device.Match);
        Router.LayerChanged += _ => ApplyLighting();
        Input.ForegroundChanged += _ => ApplyLighting();
        Hid.ConnectionChanged += connected => { if (connected) ApplyLighting(force: true); };
        // Read the battery as soon as the pad appears (after the lighting frames), and clear it when it goes.
        Hid.ConnectionChanged += _ => _batteryTimer?.Change(500, BatteryPollMs);
        Hid.ConnectionChanged += _ => { foreach (var client in Clients.All) client.Sink.Send(ProfileMessage("profile", client)); };
    }

    public void Start()
    {
        WarmUp();
        Volume.Start();
        Input.Start();
        Hid.Start();
        Pipe?.Start();
        ApplyLighting(force: true);
        _batteryTimer = new Timer(_ => PollBattery(), null, 2_000, BatteryPollMs);
        _log.LogInformation("MacroHub engine started, config {Path}, hook={Hook}, volumeGuard={Vol}", _configPath, Input.HookInstalled, Volume.Available);
    }

    /// <summary>
    /// JIT and serializer metadata cost several ms on first use. The first physical press after start must arm
    /// suppression within ~1 ms, so exercise the hot paths once up front.
    /// </summary>
    private void WarmUp()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var control = Router.Config.Device.Controls.FirstOrDefault()?.Id ?? "K1";
        var route = Router.Route(control, ControlPhase.Down, "warmup.exe");
        Router.ControlForSignature("vendor:0");
        Router.VksForControl(control);
        Router.ControlsForVk(0x41);
        Router.ConsumerUsagesForControl(control);
        Router.MatchApp("warmup.exe");
        var c = new SuppressionCorrelator();
        c.Expect(control, [0x41], true, 0);
        c.Classify(0x41, false, true, 1, out _);
        c.Classify(0x41, true, true, 2, out _);
        c.NotePassed(0x42, 3);
        new ControlStepper().OnDown(control, Router.GroupOf(control), Router.ResolveInput(control, route.Layer), 0);
        new VendorBitmapDecoder().Decode(new byte[] { 5, 1, 0 });
        _armedSignal.Reset();
        _armedSignal.Wait(0);
        _ = HubClock.MsPrecise;
        foreach (var evt in new object[]
        {
            new { type = "signal", signature = "vendor:0", phase = ControlPhase.Down, control = (string?)control, source = "hid" },
            new { type = "control", control, phase = ControlPhase.Down, source = "pad", route.Layer, route.Function, route.App, action = route.Action.Describe(), block = route.Block, forwarded = 0, executed = false, raw = false, foreground = "", gated = false, step = (string?)null },
            new { type = "suppressed", control = (string?)control, key = VirtualKeys.NameOf(0x41), up = false },
            ControlMessage(control, ControlPhase.Down, "pad", 0),
        })
            Bus.Publish(evt);
        _log.LogInformation("warm-up {Ms} ms", sw.ElapsedMilliseconds);
    }

    private HubConfig LoadOrSeed(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(DefaultsPath(), path);
            _log.LogInformation("seeded config {Path}", path);
        }
        var c = HubConfig.Load(path);
        foreach (var e in c.Validate()) _log.LogWarning("config: {Error}", e);
        return c;
    }

    /// <summary>Default configuration in <paramref name="lang"/> ("zh" / "en"), or the Windows display language.</summary>
    public static HubConfig LoadDefaults(string? lang = null) => HubConfig.Load(DefaultsPath(lang));

    /// <summary>(Chinese, English) display-name pairs of the two default configurations, for the web UI.</summary>
    public static List<(string Zh, string En)> DefaultNamePairs() => DefaultNames.Pairs(
        JsonNode.Parse(File.ReadAllText(DefaultsPath("zh"))), JsonNode.Parse(File.ReadAllText(DefaultsPath("en"))));

    /// <summary>
    /// Default configuration file: hub.json (Chinese) or hub.en.json (English), by <paramref name="lang"/> or else the
    /// Windows display language. The two differ only in display names; a unit test keeps their structure identical.
    /// </summary>
    private static string DefaultsPath(string? lang = null) => Path.Combine(AppContext.BaseDirectory, "defaults",
        (lang ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "zh" ? "hub.json" : "hub.en.json");

    public List<string> ApplyConfig(HubConfig config, bool save = true)
    {
        config.Normalize();
        var errors = config.Validate();
        if (errors.Count > 0) return errors;
        bool matchChanged = !config.Device.Match.SequenceEqual(Router.Config.Device.Match);
        Router.Apply(config);
        _correlator.Reset();
        _stepper.Reset();
        StopAllRepeats();
        if (save) config.Save(_configPath);
        if (matchChanged) Hid.Reconnect();
        Bus.Publish(new { type = "config" });
        foreach (var client in Clients.All) client.Sink.Send(ProfileMessage("profile", client));
        ApplyLighting(force: true); // the new configuration may carry a different backlight
        return errors;
    }

    public object State()
    {
        var fg = Input.Foreground;
        return new
        {
            connected = Hid.Connected,
            transport = Hid.Transport,
            battery = BatteryMessage(),
            collections = Hid.Collections,
            hook = Input.HookInstalled,
            suppression = Router.Config.Suppression,
            correlateWaitMs = CorrelateWaitMs,
            stats = new { Counters.PhysicalDowns, Counters.Suppressed, Counters.Leaked, Counters.WaitedForReport, Counters.MaxWaitMs },
            manualLayer = Router.ManualLayer,
            layer = Router.EffectiveLayer(fg.Process),
            foreground = new { fg.Process, fg.Pid, fg.Title, app = Router.MatchApp(fg.Process)?.Id },
            clients = Clients.All.Select(c => c.Describe()),
            protocol = ProtocolVersion,
            pipe = Pipe?.FullName,
            volumeGuard = Volume.Status(),
            learning = _learn?.Control,
            version = Version,
            elevated = Environment.IsPrivilegedProcess,
            configPath = _configPath,
        };
    }

    // ───────────────────────── physical signals (HID reader threads) ─────────────────────────

    private void OnPhysicalSignal(Signal s, long now)
    {
        string? control = Router.ControlForSignature(s.Signature);
        try
        {
            NudgeBattery(now);
            if (HandleLearnPhysical(s, now)) return;
            if (control is null) return;
            // In "codes" mode the hook routes keyed controls itself; physical signals only drive controls that type nothing.
            if (Router.Config.Suppression == "codes" && Router.VksForControl(control).Count > 0) return;
            HandleControl(control, s.Phase, "pad", now); // arms suppression first — the hook is ~1 ms behind
        }
        finally
        {
            Bus.Publish(new { type = "signal", signature = s.Signature, phase = s.Phase, control, source = "hid" });
        }
    }

    /// <summary>Route and execute one logical control event. Source: pad, hook, simulate.</summary>
    public RouteResult HandleControl(string control, ControlPhase phase, string source, long now)
    {
        var fg = Input.Foreground;
        RouteResult route;
        List<AppClient> targets;
        if (phase == ControlPhase.Down)
        {
            var app = Router.MatchApp(fg.Process);
            var clients = Clients.Matching(fg);
            if (app?.Forward == AppProfile.ForwardControls && clients.Count > 0)
            {
                // The app decides by its own context (e.g. UE editor tab); skip the Hub layers, block the original input.
                route = new RouteResult(control, phase, Router.EffectiveLayer(fg.Process), null, app.Id, new ForwardAction(), true, RawControls: true);
                targets = clients;
            }
            else
            {
                route = Router.Route(control, phase, fg.Process);
                targets = route.Forward ? clients : [];
            }
            _downRoutes[control] = route;
            _downTargets[control] = targets;

            if (source == "pad")
            {
                Interlocked.Increment(ref Counters.PhysicalDowns);
                if (Router.Config.Suppression == "correlate")
                {
                    // Only controls reported through the vendor bitmap can type keys; consumer controls (knob rotation)
                    // never reach the keyboard hook, so a wildcard there would only risk swallowing main-keyboard keys.
                    bool wildcard = Router.Config.Device.Controls.FirstOrDefault(c => c.Id == control)?.Signatures.Any(sig => sig.StartsWith("vendor:")) ?? false;
                    int leaked = _correlator.Expect(control, Router.VksForControl(control), route.Block, now, wildcard);
                    if (leaked > 0)
                    {
                        Interlocked.Add(ref Counters.Leaked, leaked);
                        Bus.Publish(new { type = "leak", control, count = leaked });
                        _log.LogWarning("{Control}: physical report arrived after the hook gave up — original key leaked", control);
                    }
                }
                // The knob's native volume change cannot be blocked; undo it when the knob is used for something else.
                if (route.Block && Router.Config.KnobVolumeGuard && Router.ConsumerUsagesForControl(control).Any(NativeConsumer.IsVolume))
                    Volume.Arm();
            }
        }
        else
        {
            route = _downRoutes.TryRemove(control, out var d) ? d with { Phase = phase } : Router.Route(control, phase, fg.Process);
            targets = _downTargets.TryRemove(control, out var t) ? t : [];
            if (source == "pad") _correlator.PhysicalUp(control);
            StopRepeat(control);
        }

        // Input shaping (detents per trigger, throttle). Raw controls always pass: the app scales them itself.
        // Suppression and the volume guard above still ran for every detent.
        InputSettings input = InputSettings.Default;
        ControlStepper.Result? step = null;
        bool gated;
        if (phase == ControlPhase.Down)
        {
            gated = false;
            if (!route.RawControls)
            {
                input = Router.ResolveInput(control, route.Layer);
                step = _stepper.OnDown(control, Router.GroupOf(control), input, now);
                gated = !step.Value.Trigger;
            }
            if (gated) _gatedDowns[control] = true; else _gatedDowns.TryRemove(control, out _);
        }
        else gated = _gatedDowns.TryRemove(control, out _);
        if (gated) targets = [];

        int delivered = 0;
        if (targets.Count > 0)
        {
            foreach (var client in targets)
            {
                object msg = route.RawControls
                    ? ControlMessage(control, phase, source, client.NextSeq())
                    : FunctionMessage(route, phase, client.NextSeq());
                if (client.Sink.Send(msg)) delivered++;
            }
        }

        // A connected client handles forwarded events natively; without one the local action is the fallback.
        bool handledByApp = delivered > 0 || route.RawControls;
        var action = route.Action;
        bool execute = !gated && !handledByApp && action is not (PassthroughAction or NoneAction or ForwardAction)
                       && (phase == ControlPhase.Down || ActionExecutor.WantsUp(action));
        if (execute) Executor.Enqueue(action, phase, control);
        if (execute && phase == ControlPhase.Down && input.Repeat && !ActionExecutor.WantsUp(action))
            StartRepeat(control, action, input);

        Bus.Publish(new
        {
            type = "control", control, phase, source, route.Layer, route.Function, route.App,
            action = route.RawControls ? "raw control → app" : action.Describe(), block = route.Block, forwarded = delivered, executed = execute,
            raw = route.RawControls, foreground = fg.Process,
            gated, step = step is { } st && st.Needed > 1 ? $"{st.Progress}/{st.Needed}" : null,
        });
        return route;
    }

    private void StartRepeat(string control, ActionSpec action, InputSettings input)
    {
        StopRepeat(control);
        var timer = new Timer(_ =>
        {
            if (!_repeaters.ContainsKey(control)) return;
            Executor.Enqueue(action, ControlPhase.Down, control);
            Bus.Publish(new { type = "repeat", control, action = action.Describe() });
        }, null, input.RepeatDelayMs, input.RepeatIntervalMs);
        _repeaters[control] = timer;
    }

    private void StopRepeat(string control)
    {
        if (_repeaters.TryRemove(control, out var timer)) timer.Dispose();
    }

    private void StopAllRepeats()
    {
        foreach (var key in _repeaters.Keys.ToList()) StopRepeat(key);
    }

    private object ControlMessage(string control, ControlPhase phase, string source, long seq)
    {
        var def = Router.Config.Device.Controls.FirstOrDefault(c => c.Id == control);
        return new { type = "control", seq, t = Math.Round(HubClock.MsPrecise, 3), control, phase, kind = def?.Kind, part = def?.Part, source };
    }

    private object FunctionMessage(RouteResult route, ControlPhase phase, long seq)
    {
        var fn = route.Function is null ? null : Router.Config.Functions.FirstOrDefault(f => f.Id == route.Function);
        return new { type = "function", seq, t = Math.Round(HubClock.MsPrecise, 3), function = route.Function, name = fn?.Name, route.Control, phase, route.Layer, route.App };
    }

    // ───────────────────────── keyboard hook (input thread) ─────────────────────────

    /// <returns>true to block the keystroke.</returns>
    private bool FilterKey(ushort vk, ushort scan, bool up, bool injected)
    {
        if (injected) return false; // the pad never injects; other tools' injections are not ours to judge
        if (HandleLearnKey(vk, up)) return true;

        var mode = Router.Config.Suppression;
        if (mode == "off") return false;
        var candidates = Router.ControlsForVk(vk);
        long now = HubClock.Ms;

        if (mode == "codes")
        {
            if (candidates.Count == 0) return false;
            // No physical channel: the key code itself identifies the control.
            string control = candidates[0];
            lock (_codesHeld)
            {
                if (!up && !_codesHeld.Add(vk)) return _downRoutes.TryGetValue(control, out var r) && r.Block; // auto-repeat
                if (up) _codesHeld.Remove(vk);
            }
            return HandleControl(control, up ? ControlPhase.Up : ControlPhase.Down, "hook", now).Block;
        }

        // correlate
        if (!Hid.Connected) return false;
        bool candidate = candidates.Count > 0;
        _armedSignal.Reset();
        var verdict = _correlator.Classify(vk, up, candidate, now, out var ctl);
        if (verdict == SuppressionCorrelator.Verdict.Unknown && !up)
        {
            long deadline = now + CorrelateWaitMs;
            while (verdict == SuppressionCorrelator.Verdict.Unknown)
            {
                long remaining = deadline - HubClock.Ms;
                if (remaining <= 0 || !_armedSignal.Wait((int)remaining)) break;
                _armedSignal.Reset();
                verdict = _correlator.Classify(vk, up, true, HubClock.Ms, out ctl);
            }
            long waited = HubClock.Ms - now;
            if (verdict != SuppressionCorrelator.Verdict.Unknown)
            {
                Interlocked.Increment(ref Counters.WaitedForReport);
                if (waited > Counters.MaxWaitMs) Counters.MaxWaitMs = waited;
            }
        }
        if (!up && verdict is SuppressionCorrelator.Verdict.Unknown or SuppressionCorrelator.Verdict.NotPad)
            _correlator.NotePassed(vk, HubClock.Ms);
        if (verdict == SuppressionCorrelator.Verdict.PadBlock)
        {
            if (!up) Interlocked.Increment(ref Counters.Suppressed);
            Bus.Publish(new { type = "suppressed", control = ctl, key = VirtualKeys.NameOf(vk), up });
            return true;
        }
        return false;
    }

    // ───────────────────────── learn wizard ─────────────────────────

    public bool StartLearn(string control)
    {
        if (Router.Config.Device.Controls.All(c => c.Id != control)) return false;
        lock (_learnGate)
        {
            _learn?.Finish?.Dispose();
            _learn = new LearnSession { Control = control, Expires = DateTime.UtcNow.AddSeconds(15) };
        }
        Bus.Publish(new { type = "learn", state = "waiting", control });
        return true;
    }

    public void CancelLearn()
    {
        lock (_learnGate) { _learn?.Finish?.Dispose(); _learn = null; }
        Bus.Publish(new { type = "learn", state = "cancelled" });
    }

    private bool HandleLearnPhysical(Signal s, long now)
    {
        lock (_learnGate)
        {
            if (_learn is null) return false;
            if (DateTime.UtcNow > _learn.Expires) { _learn = null; Bus.Publish(new { type = "learn", state = "timeout" }); return false; }
            if (s.Phase == ControlPhase.Down)
            {
                _learn.Physical.Add(s.Signature);
                _learn.LastPhysical = now;
                _learn.Held++;
                _learn.Finish?.Dispose();
            }
            else
            {
                _learn.Held = Math.Max(0, _learn.Held - 1);
                if (_learn.Held == 0) ScheduleLearnFinish();
            }
            return true;
        }
    }

    private bool HandleLearnKey(ushort vk, bool up)
    {
        lock (_learnGate)
        {
            if (_learn is null) return false;
            long now = HubClock.Ms;
            bool physicalChannel = Hid.Connected;
            // With a physical channel only keys right after a physical press belong to the pad.
            if (physicalChannel && now - _learn.LastPhysical > 150) return false;
            if (!up)
            {
                _learn.Keys.Add(Signal.SignatureOf(SignalSource.Key, vk));
                if (!physicalChannel) ScheduleLearnFinish();
            }
            return true; // swallow while learning
        }
    }

    private void ScheduleLearnFinish()
    {
        _learn!.Finish?.Dispose();
        _learn.Finish = new Timer(_ => FinishLearn(), null, 250, Timeout.Infinite);
    }

    private void FinishLearn()
    {
        LearnSession? session;
        lock (_learnGate) { session = _learn; _learn = null; }
        if (session is null || session.Physical.Count + session.Keys.Count == 0) return;

        var config = Router.Config.Clone();
        var learned = session.Physical.Concat(session.Keys).ToList();
        foreach (var c in config.Device.Controls)
            c.Signatures.RemoveAll(sig => session.Physical.Contains(sig)); // a physical signature identifies exactly one control
        var target = config.Device.Controls.First(c => c.Id == session.Control);
        target.Signatures = learned;
        var errors = ApplyConfig(config);
        Bus.Publish(new { type = "learn", state = errors.Count == 0 ? "done" : "error", control = session.Control, signatures = learned, errors });
    }

    // ───────────────────────── app clients (WebSocket and named pipe) ─────────────────────────

    /// <summary>First message the Hub sends on a new application connection.</summary>
    public object HubHello() => new { type = "hello", role = "hub", protocol = ProtocolVersion, product = "MacroHub", version = Version };

    /// <summary>Handle one JSON message from a WebSocket or pipe connection.</summary>
    public void HandleClientMessage(Guid id, string text, IClientSink sink)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(text); }
        catch (JsonException) { sink.Send(new { type = "error", message = "invalid JSON" }); return; }
        switch ((string?)msg?["type"])
        {
            case "hello" when (string?)msg["role"] == "app":
            {
                uint pid = (uint?)msg["pid"] ?? 0;
                string process = (string?)msg["process"] ?? "";
                if (pid != 0 && process.Length == 0) process = InputThread.ProcessName(pid);
                var client = new AppClient
                {
                    Id = id, App = (string?)msg["app"] ?? "app", Process = process, Pid = pid,
                    Mode = (string?)msg["mode"] ?? "", Protocol = (int?)msg["protocol"] ?? 1, Sink = sink,
                };
                Clients.Add(client);
                _log.LogInformation("app client connected: {App} {Process} pid={Pid} mode={Mode} v{Protocol} via {Transport}",
                    client.App, client.Process, client.Pid, client.Mode, client.Protocol, sink.Transport);
                sink.Send(ProfileMessage("welcome", client));
                PublishClients();
                break;
            }
            case "context":
                if (Clients.Get(id) is { } c)
                {
                    c.Context = (string?)msg["name"];
                    c.ContextDetail = (string?)msg["detail"];
                    PublishClients();
                }
                break;
            case "simulate":
                if ((string?)msg["control"] is { } control)
                {
                    var phase = (string?)msg["phase"] == "up" ? ControlPhase.Up : ControlPhase.Down;
                    HandleControl(control, phase, "simulate", HubClock.Ms);
                }
                break;
            // An application can take over the backlight while it is connected (the UE plugin tints the pad per
            // editor context). {"type":"lighting","reset":true} hands it back to the Hub configuration.
            case "lighting":
            {
                if ((bool?)msg["reset"] == true)
                {
                    ApplyLighting(force: true);
                    break;
                }
                var spec = new LightingSpec
                {
                    Mode = (int?)msg["mode"] ?? 1,
                    Brightness = (int?)msg["brightness"] ?? 5,
                    Speed = (int?)msg["speed"] ?? 3,
                    Direction = (int?)msg["direction"] ?? 0,
                    Color = (string?)msg["color"] ?? "#ffffff",
                }.Sanitize();
                Task.Run(() =>
                {
                    try { Lighting.Apply(spec, force: true); }
                    catch (Exception e) { _log.LogWarning(e, "client lighting failed"); }
                });
                break;
            }
            case "ping":
                sink.Send(new { type = "pong", t = Math.Round(HubClock.MsPrecise, 3), echo = msg["t"]?.DeepClone() });
                break;
        }
    }

    /// <summary>welcome/profile: what the Hub will send this client, plus the control list so it can build bindings.</summary>
    private object ProfileMessage(string type, AppClient client)
    {
        var app = Router.MatchApp(client.Process);
        return new
        {
            type, protocol = ProtocolVersion, clientId = client.Id,
            profile = app is null ? null : new { app.Id, app.Name, app.Forward, app.Layer },
            // Lets a client show whether the keyboard itself is connected, not just the Hub.
            padConnected = Hid.Connected,
            padTransport = Hid.Transport,
            // Rect lets a client draw the device the way the web UI does (x, y, width, height in key units).
            controls = Router.Config.Device.Controls.Select(c => new { c.Id, c.Label, c.Kind, c.Part, c.Rect }),
        };
    }

    public void UnregisterClient(Guid id)
    {
        if (Clients.Remove(id, out var c))
        {
            _log.LogInformation("app client disconnected: {App} via {Transport}", c!.App, c.Sink.Transport);
            PublishClients();
        }
    }

    private void PublishClients() => Bus.Publish(new { type = "clients", clients = Clients.All.Select(c => c.Describe()) });

    public void Dispose()
    {
        _batteryTimer?.Dispose();
        StopAllRepeats();
        Pipe?.Dispose();
        Hid.Dispose();
        Input.Dispose();
        Executor.Dispose();
        Volume.Dispose();
    }
}
