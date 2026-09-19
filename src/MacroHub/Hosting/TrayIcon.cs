using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MacroHub.Core;

namespace MacroHub.Hosting;

/// <summary>
/// Notification-area icon: the Hub has no window of its own, so this is where the user sees that it is running,
/// what state it is in (the same facts as the web UI's top bar), opens the page and quits it.
/// Runs its own STA thread with a WinForms message loop; everything else in the Hub stays on its own threads.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly HubEngine _engine;
    private readonly string _uiUrl;
    private readonly string _logDir;
    private readonly Action _exit;
    private readonly ILogger _log;
    private Thread? _thread;
    private ApplicationContext? _context;
    private NotifyIcon? _icon;
    /// <summary>Handle on the tray thread, so shutdown can be marshalled onto it.</summary>
    private Control? _invoker;
    private System.Windows.Forms.Timer? _refresh;

    private (int?, bool?)? _shown;

    private enum Health { Ok, NoPad, NoHook }

    /// <summary>The tray follows the Windows display language: Chinese on a Chinese UI, English otherwise.</summary>
    private static readonly bool Chinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    private static string L(string zh, string en) => Chinese ? zh : en;
    private static string Transport(string transport) => PadTransport.Label(transport, Chinese);

    public TrayIcon(HubEngine engine, string uiUrl, string logDir, Action exit, ILogger log)
    {
        _engine = engine;
        _uiUrl = uiUrl;
        _logDir = logDir;
        _exit = exit;
        _log = log;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "TrayIcon" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            Application.EnableVisualStyles();
            _context = new ApplicationContext();
            _invoker = new Control();
            _ = _invoker.Handle;

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Opening += (_, _) => FillMenu(menu);
            FillMenu(menu);

            _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenUi(); };
            UpdateIcon();

            // Cheap snapshot of a few properties; keeps the gauge and tooltip current without events from every thread.
            _refresh = new System.Windows.Forms.Timer { Interval = 1500 };
            _refresh.Tick += (_, _) => UpdateIcon();
            _refresh.Start();

            Application.Run(_context);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "tray icon failed; the Hub keeps running without it");
        }
    }

    // ───────────── state ─────────────

    private Health CurrentHealth() =>
        !_engine.Input.HookInstalled ? Health.NoHook : _engine.Hid.Connected ? Health.Ok : Health.NoPad;

    private string LayerName()
    {
        var id = _engine.Router.EffectiveLayer(_engine.Input.Foreground.Process);
        return _engine.Router.Config.Layers.FirstOrDefault(l => l.Id == id)?.Name ?? id;
    }

    private void UpdateIcon()
    {
        if (_icon is null) return;
        var health = CurrentHealth();
        var gauge = health == Health.Ok ? _engine.Battery : null;
        // Redraw only when something visible changed; each icon is a GDI handle that must be released.
        var look = (gauge?.Percent, gauge?.Charging);
        if (look != _shown)
        {
            var previous = _icon.Icon;
            _icon.Icon = DrawIcon(gauge);
            if (previous is not null) DestroyIcon(previous);
            _shown = look;
        }
        string battery = _engine.Battery is { } b ? $" {BatteryText(b)}" : "";
        string pad = health switch
        {
            Health.Ok => L($"键盘在线（{Transport(_engine.Hid.Transport)}{battery}）", $"pad online ({Transport(_engine.Hid.Transport)}{battery})"),
            Health.NoPad => L("键盘未连接", "pad not connected"),
            _ => L("键盘钩子未安装", "keyboard hook not installed"),
        };
        // NotifyIcon.Text is limited to 127 characters.
        string text = $"MacroHub {_engine.Version} · {pad} · {LayerName()}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void FillMenu(ContextMenuStrip menu)
    {
        var config = _engine.Router.Config;
        var fg = _engine.Input.Foreground;
        var clients = _engine.Clients.All;
        var ok = Color.FromArgb(0x1f, 0x9d, 0x55);
        var warn = Color.FromArgb(0xd9, 0x80, 0x0b);
        var off = Color.Gray;

        menu.Items.Clear();
        menu.Items.Add(new ToolStripLabel($"MacroHub {_engine.Version}") { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());

        // Same facts as the web UI's top bar.
        Status(menu, _engine.Hid.Connected ? L($"● 宏键盘在线 · {Transport(_engine.Hid.Transport)}", $"● Pad online · {Transport(_engine.Hid.Transport)}") : L("○ 宏键盘未连接", "○ Pad not connected"), _engine.Hid.Connected ? ok : warn);
        if (_engine.Hid.Connected && _engine.Battery is { } battery)
            Status(menu, L($"   电量：{BatteryText(battery)}", $"   Battery: {BatteryText(battery)}"), !battery.Charging && battery.Percent <= 20 ? warn : menu.ForeColor);
        Status(menu, _engine.Input.HookInstalled ? L("● 键盘钩子已安装", "● Keyboard hook installed") : L("○ 键盘钩子未安装", "○ Keyboard hook not installed"), _engine.Input.HookInstalled ? ok : warn);
        Status(menu, L("   拦截：", "   Interception: ") + config.Suppression switch { "correlate" => L("关联", "correlate"), "codes" => L("按键码", "key codes"), _ => L("关闭", "off") }, menu.ForeColor);
        Status(menu, L("   当前层：", "   Layer: ") + LayerName(), menu.ForeColor);
        Status(menu, L("   前台：", "   Foreground: ") + (string.IsNullOrEmpty(fg.Process) ? "—" : fg.Process), menu.ForeColor);
        Status(menu, clients.Count == 0
                ? L("○ 应用客户端：无", "○ App clients: none")
                : L($"● 应用客户端 {clients.Count}：", $"● App clients {clients.Count}: ") + string.Join(L("、", ", "), clients.Select(c => string.IsNullOrEmpty(c.Process) ? c.App : c.Process).Distinct()),
            clients.Count == 0 ? off : ok);
        Status(menu, config.Lighting.Enabled ? L("● 灯光：由 MacroHub 接管", "● Lighting: driven by MacroHub") : L("○ 灯光：未接管", "○ Lighting: not taken over"), config.Lighting.Enabled ? ok : off);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L("打开配置页", "Open setup page"), null, (_, _) => OpenUi()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem(L("帮助文档", "Help"), null, (_, _) => Open(_uiUrl + "help.html")));
        menu.Items.Add(new ToolStripMenuItem(L("打开日志文件夹", "Open log folder"), null, (_, _) => Open(_logDir)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L("退出 MacroHub", "Quit MacroHub"), null, (_, _) => _exit()));
    }

    private static string BatteryText(PadBattery battery) =>
        battery.Charging ? L($"充电中 {battery.Percent}%", $"charging {battery.Percent}%") : $"{battery.Percent}%";

    private static void Status(ContextMenuStrip menu, string text, Color color) =>
        menu.Items.Add(new ToolStripLabel(text) { ForeColor = color, Padding = new Padding(0, 1, 0, 1) });

    private void OpenUi() => Open(_uiUrl);

    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { _log.LogWarning("could not open {Target}: {Message}", target, e.Message); }
    }

    // ───────────── icon ─────────────

    // Ring open at the bottom: it starts at the lower left (140°, GDI+ angles run clockwise from 3 o'clock) and
    // sweeps over the top to the lower right, leaving a 100° gap at the bottom.
    private const float RingStart = 140f;
    private const float RingSweep = 260f;

    /// <summary>
    /// The web UI's yellow diamond inside an open ring. The ring is the battery gauge, filling clockwise from the
    /// lower left (green, amber at 20 % and below, red at 10 % and below). Without a reading (pad away, or not
    /// answered yet) only the grey track is drawn, which is also how a missing pad shows.
    /// </summary>
    private static Icon DrawIcon(PadBattery? battery)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var ring = new RectangleF(3.5f, 3.5f, 25f, 25f);
            // A dark edge one pixel wide on each side, like the diamond's outline, so the gauge holds on any taskbar.
            using (var edge = new Pen(Color.FromArgb(0x1a, 0x1a, 0x1a), 5.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(edge, ring, RingStart, RingSweep);
            // Opaque, or the edge would show through and darken the whole track.
            using (var track = new Pen(Color.FromArgb(0x7d, 0x82, 0x8b), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(track, ring, RingStart, RingSweep);
            if (battery is { } b && b.Percent > 0)
            {
                var level = b.Charging || b.Percent > 20 ? Color.FromArgb(0x34, 0xc7, 0x7b)
                    : b.Percent > 10 ? Color.FromArgb(0xff, 0x9f, 0x43)
                    : Color.FromArgb(0xff, 0x4d, 0x5e);
                using var fill = new Pen(level, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(fill, ring, RingStart, RingSweep * Math.Clamp(b.Percent, 0, 100) / 100f);
            }

            // Sits a little low so its bottom tip fills the ring's opening.
            var diamond = new[] { new PointF(16, 7.5f), new PointF(25, 16.5f), new PointF(16, 25.5f), new PointF(7, 16.5f) };
            using (var fill = new SolidBrush(Color.FromArgb(0xf5, 0xc4, 0x00))) g.FillPolygon(fill, diamond);
            using (var edge = new Pen(Color.FromArgb(0x1a, 0x1a, 0x1a), 1.2f)) g.DrawPolygon(edge, diamond);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Icons from <see cref="Bitmap.GetHicon"/> own a GDI handle that Dispose does not free.</summary>
    private static void DestroyIcon(Icon icon)
    {
        DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    public void Dispose()
    {
        var context = _context;
        var icon = _icon;
        var invoker = _invoker;
        if (context is null || icon is null || invoker is null) return;
        try
        {
            // The icon belongs to the tray thread: hide it there, then end its message loop.
            invoker.BeginInvoke(() =>
            {
                _refresh?.Stop();
                icon.Visible = false;
                icon.Dispose();
                context.ExitThread();
            });
            _thread?.Join(2000);
        }
        catch (Exception e)
        {
            _log.LogDebug(e, "tray icon shutdown");
        }
    }
}
