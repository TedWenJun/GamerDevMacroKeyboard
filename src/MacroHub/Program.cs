using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroHub;
using MacroHub.Clients;
using System.Diagnostics;
using System.Reflection;
using MacroHub.Core;
using MacroHub.Hosting;
using MacroHub.Input;
using MacroHub.Output;

// MacroHub — system-layer daemon for the macro pad.
//   --config <path>   config file (default %APPDATA%\MacroHub\hub.json, seeded from defaults\hub.json)
//   --port <n>        web UI / WebSocket port on 127.0.0.1 (default 17900)
//   --pipe <name>     application named pipe \\.\pipe\<name> (default MacroHub)
//   --log-dir <path>  log directory (default %LOCALAPPDATA%\MacroHub\logs)
//   --open            open the web UI in the browser (also when an instance is already running)
//   --test-mode       enables /api/diag/foreground to pin the routing foreground (tests/e2e only); implies --no-tray
//   --no-tray         run without the notification-area icon (headless / service use)
string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

var version = typeof(HubEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
var configPath = Path.GetFullPath(Arg("--config", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroHub", "hub.json")));
int port = int.Parse(Arg("--port", "17900"));
string pipeName = Arg("--pipe", "MacroHub");
string logDir = Path.GetFullPath(Arg("--log-dir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroHub", "logs")));
bool testMode = args.Contains("--test-mode");
bool openBrowser = args.Contains("--open");
bool showTray = !testMode && !args.Contains("--no-tray");
string uiUrl = $"http://127.0.0.1:{port}/";

void OpenUi()
{
    try { Process.Start(new ProcessStartInfo(uiUrl) { UseShellExecute = true }); } catch { }
}

// One Hub per pipe name: two instances would both hook the keyboard and fight over the pad.
using var singleInstance = new Mutex(true, $@"Local\MacroHub-{pipeName}", out bool firstInstance);
if (!firstInstance)
{
    Console.Error.WriteLine($"MacroHub is already running ({uiUrl}).");
    if (openBrowser) OpenUi();
    return 3;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; });
builder.Logging.AddProvider(new FileLoggerProvider(logDir));
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = HubConfig.JsonOptions.PropertyNamingPolicy;
    o.SerializerOptions.DefaultIgnoreCondition = HubConfig.JsonOptions.DefaultIgnoreCondition;
    foreach (var c in HubConfig.JsonOptions.Converters) o.SerializerOptions.Converters.Add(c);
});
builder.Services.AddSingleton(sp => new HubEngine(sp.GetRequiredService<ILogger<HubEngine>>(), sp.GetRequiredService<ILoggerFactory>(), configPath));

var app = builder.Build();
var engine = app.Services.GetRequiredService<HubEngine>();
engine.Pipe = new PipeServer(pipeName, engine, app.Services.GetRequiredService<ILogger<PipeServer>>());
engine.Start();
app.Lifetime.ApplicationStopping.Register(engine.Dispose);

// Notification-area icon: status at a glance, open the page, quit.
TrayIcon? tray = null;
if (showTray)
{
    tray = new TrayIcon(engine, uiUrl, logDir, () => app.Lifetime.StopApplication(), app.Services.GetRequiredService<ILogger<TrayIcon>>());
    tray.Start();
    app.Lifetime.ApplicationStopping.Register(tray.Dispose);
}
engine.Version = version;

app.UseMiddleware<LocalOriginGuard>(port);
app.UseWebSockets();
app.UseDefaultFiles();
// Local tool whose UI changes with every build: always revalidate so edits are never hidden by the browser cache.
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".md"] = "text/markdown; charset=utf-8";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.MapGet("/help", () => Results.Redirect("/help.html"));

var api = app.MapGroup("/api");
api.MapGet("/state", () => Results.Json(engine.State(), HubConfig.JsonOptions));
api.MapGet("/config", () => Results.Text(JsonSerializer.Serialize(engine.Router.Config, HubConfig.JsonOptions), "application/json"));
api.MapPut("/config", async (HttpRequest req) =>
{
    HubConfig? config;
    try { config = await JsonSerializer.DeserializeAsync<HubConfig>(req.Body, HubConfig.JsonOptions); }
    catch (JsonException e) { return Results.BadRequest(new { errors = new[] { e.Message } }); }
    if (config is null) return Results.BadRequest(new { errors = new[] { "empty body" } });
    var errors = engine.ApplyConfig(config);
    return errors.Count == 0 ? Results.Ok(new { ok = true }) : Results.BadRequest(new { errors });
});
api.MapPost("/config/validate", async (HttpRequest req) =>
{
    try
    {
        var config = await JsonSerializer.DeserializeAsync<HubConfig>(req.Body, HubConfig.JsonOptions);
        return Results.Ok(new { errors = config?.Normalize().Validate() ?? ["empty body"] });
    }
    catch (JsonException e) { return Results.Ok(new { errors = new[] { e.Message } }); }
});
api.MapPost("/config/reset", (string? lang) =>
{
    var errors = engine.ApplyConfig(HubEngine.LoadDefaults(lang is "zh" or "en" ? lang : null));
    return errors.Count == 0 ? Results.Ok(new { ok = true }) : Results.BadRequest(new { errors });
});
var defaultNames = HubEngine.DefaultNamePairs().Select(p => new[] { p.Zh, p.En }).ToList();
api.MapGet("/names", () => defaultNames);
api.MapGet("/keys", () => VirtualKeys.AllNames);
api.MapGet("/devices", () => PadHidReader.Find([""]).GroupBy(c => c.Path.Split('#').ElementAtOrDefault(1) ?? c.Path)
    .Select(g => new { id = g.Key, collections = g.Select(c => new { c.UsagePage, c.Usage, c.InputLength }) }));
api.MapPost("/simulate", (SimulateRequest r) =>
{
    var route = engine.HandleControl(r.Control, r.Phase, "simulate", HubClock.Ms);
    if (r.Phase == ControlPhase.Down && r.Tap)
        engine.HandleControl(r.Control, ControlPhase.Up, "simulate", HubClock.Ms);
    return Results.Json(route, HubConfig.JsonOptions);
});
api.MapPost("/execute", (ExecuteRequest r) =>
{
    engine.Executor.Enqueue(r.Action, ControlPhase.Down, "test");
    if (ActionExecutor.WantsUp(r.Action)) engine.Executor.Enqueue(r.Action, ControlPhase.Up, "test");
    return Results.Ok(new { queued = r.Action.Describe() });
});
api.MapPost("/layer", (LayerAction op) => { engine.Router.SetLayer(op); return Results.Ok(new { layer = engine.Router.ManualLayer }); });
api.MapPost("/learn/{control}", (string control) => engine.StartLearn(control) ? Results.Ok() : Results.NotFound());
api.MapDelete("/learn", () => { engine.CancelLearn(); return Results.Ok(); });
api.MapDelete("/stats", () => { engine.ResetStats(); return Results.Ok(); });
if (testMode)
    api.MapPost("/diag/foreground", (PinRequest r) =>
    {
        engine.Input.Pin(r.Pid is null ? null : new ForegroundInfo(r.Pid.Value, r.Process ?? InputThread.ProcessName(r.Pid.Value), "(pinned by test)"));
        return Results.Ok(new { pinned = r.Pid is not null });
    });
api.MapGet("/diag/volume", () => Results.Json(engine.Volume.Status(), HubConfig.JsonOptions));
api.MapPost("/diag/volume/arm", () => { engine.Volume.Arm(); return Results.Json(engine.Volume.Status(), HubConfig.JsonOptions); });
// Lighting: the pad's backlight. GET reports what the device was last given plus the effect names the firmware has.
api.MapGet("/lighting", () => Results.Json(new
{
    available = engine.Lighting.Available,
    config = engine.Router.Config.Lighting,
    effective = engine.EffectiveLighting(),
    applied = engine.Lighting.Applied,
    modes = LightingSpec.ModeNames,
}, HubConfig.JsonOptions));

// Preview a state without saving it, so dragging a slider in the UI shows up on the pad straight away.
api.MapPost("/lighting/preview", (LightingSpec spec) =>
    Results.Json(new { applied = engine.Lighting.Apply(spec.Sanitize(), force: true) }, HubConfig.JsonOptions));

// Re-apply whatever the configuration says for the active layer (used after saving, and to undo a preview).
api.MapPost("/lighting/apply", () =>
{
    engine.ApplyLighting(force: true);
    return Results.Json(new { effective = engine.EffectiveLighting() }, HubConfig.JsonOptions);
});

api.MapPost("/tuning", (TuningRequest t) => { engine.CorrelateWaitMs = Math.Clamp(t.CorrelateWaitMs, 0, 50); return Results.Ok(); });

// WebSocket: the web UI subscribes to events; applications send {"type":"hello","role":"app",...} to receive function events.
app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var (id, reader) = engine.Bus.Subscribe();
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    var sender = Task.Run(async () =>
    {
        try
        {
            await foreach (var msg in reader.ReadAllAsync(cts.Token))
                await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception) { }
    });
    var sink = new BusSink(engine.Bus, id);
    engine.Bus.SendTo(id, new { type = "hello", role = "hub", protocol = HubEngine.ProtocolVersion, version = 1, state = engine.State() });
    var buffer = new byte[16 * 1024];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(buffer, cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buffer, 0, res.Count);
            } while (!res.EndOfMessage);
            if (res.MessageType == WebSocketMessageType.Close) break;
            engine.HandleClientMessage(id, Encoding.UTF8.GetString(ms.ToArray()), sink);
        }
    }
    catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
    finally
    {
        engine.UnregisterClient(id);
        engine.Bus.Unsubscribe(id);
        cts.Cancel();
        await sender;
    }
});

app.Logger.LogInformation("MacroHub {Version} web UI: {Url} · pipe \\\\.\\pipe\\{Pipe} · logs {LogDir}", version, uiUrl, pipeName, logDir);
if (openBrowser) app.Lifetime.ApplicationStarted.Register(OpenUi);
app.Run();
return 0;

record SimulateRequest(string Control, ControlPhase Phase = ControlPhase.Down, bool Tap = true);
record ExecuteRequest(ActionSpec Action);
record TuningRequest(int CorrelateWaitMs);
record PinRequest(uint? Pid, string? Process);
