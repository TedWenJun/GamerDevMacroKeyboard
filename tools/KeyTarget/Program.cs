using System.Runtime.InteropServices;

// KeyTarget: a test window that takes the foreground and writes every key/char message it receives
// to stdout as "down 0x11", "up 0x46", "char 0x4E2D" lines. Used by tests/e2e/e2e.mjs.
//   KeyTarget.exe [seconds]
int seconds = args.Length > 0 ? int.Parse(args[0]) : 20;
ApplicationConfiguration.Initialize();
var form = new TargetForm { Text = "MacroHub KeyTarget", Width = 420, Height = 160, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
timer.Tick += (_, _) => form.Close();
timer.Start();
form.Shown += (_, _) => Foreground.Force(form.Handle);
Application.Run(form);

sealed class TargetForm : Form
{
    private readonly Label _label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "MacroHub e2e key target" };
    public TargetForm() { Controls.Add(_label); KeyPreview = true; }

    protected override bool ProcessKeyPreview(ref Message m) => base.ProcessKeyPreview(ref m);

    protected override void WndProc(ref Message m)
    {
        Log(m);
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // swallow Alt combos so they never open the system menu; everything else continues to WM_CHAR translation
        return (keyData & Keys.Alt) == Keys.Alt || keyData == Keys.F10;
    }

    public override bool PreProcessMessage(ref Message msg)
    {
        Log(msg);
        return base.PreProcessMessage(ref msg);
    }

    private int _lastMsg; private IntPtr _lastW, _lastL; private int _lastTime;

    private void Log(Message m)
    {
        string? kind = m.Msg switch { 0x100 or 0x104 => "down", 0x101 or 0x105 => "up", 0x102 or 0x106 => "char", _ => null };
        if (kind is null) return;
        // PreProcessMessage and WndProc may both see a message; drop exact duplicates
        int t = Environment.TickCount;
        if (m.Msg == _lastMsg && m.WParam == _lastW && m.LParam == _lastL && t - _lastTime < 5) return;
        (_lastMsg, _lastW, _lastL, _lastTime) = (m.Msg, m.WParam, m.LParam, t);
        // dwExtraInfo of the message being processed: MacroHub tags its injected input with "MHKB"
        bool fromHub = Foreground.GetMessageExtraInfo() == 0x4D484B42;
        Console.WriteLine($"{kind} 0x{m.WParam.ToInt64():X2}{(fromHub ? " hub" : "")}");
        Console.Out.Flush();
    }
}

static class Foreground
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);

    public static void Force(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        if (fgThread != me) AttachThreadInput(me, fgThread, true);
        BringWindowToTop(hwnd);
        bool ok = SetForegroundWindow(hwnd);
        if (fgThread != me) AttachThreadInput(me, fgThread, false);
        Console.WriteLine($"ready foreground={ok && GetForegroundWindow() == hwnd} pid={Environment.ProcessId}");
        Console.Out.Flush();
    }
}
