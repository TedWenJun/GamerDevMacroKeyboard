using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MacroHub.Native;

namespace MacroHub.Input;

public readonly record struct ForegroundInfo(uint Pid, string Process, string Title);

/// <summary>
/// Dedicated message-loop thread owning the WH_KEYBOARD_LL hook and the foreground WinEvent hook.
/// The hook callback delegates the block decision to <see cref="KeyFilter"/>; it must stay fast.
/// </summary>
public sealed class InputThread : IDisposable
{
    /// <summary>Marker put into dwExtraInfo of everything MacroHub injects, so the hook ignores its own output.</summary>
    public static readonly IntPtr InjectMarker = 0x4D48_4B42; // "MHKB"

    public delegate bool KeyFilterFn(ushort vk, ushort scan, bool up, bool injected);

    private readonly ILogger _log;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook, _winEventHook;
    // keep delegates alive for the lifetime of the hooks
    private User32.HookProc? _hookProc;
    private User32.WinEventProc? _winEventProc;
    private readonly ManualResetEventSlim _ready = new();

    public KeyFilterFn? KeyFilter { get; set; }
    public event Action<ForegroundInfo>? ForegroundChanged;
    private ForegroundInfo _foreground;
    private ForegroundInfo? _pinned;

    /// <summary>The foreground app used for routing (the pinned one in test mode, see <see cref="Pin"/>).</summary>
    public ForegroundInfo Foreground => _pinned ?? _foreground;

    /// <summary>Test mode only: route as if this app were in the foreground; null restores real tracking.</summary>
    public void Pin(ForegroundInfo? info)
    {
        _pinned = info;
        ForegroundChanged?.Invoke(Foreground);
    }
    public bool HookInstalled => _kbHook != IntPtr.Zero;

    public InputThread(ILogger log) => _log = log;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "MacroHub Input" };
        _thread.Start();
        _ready.Wait(5000);
    }

    private void Run()
    {
        _threadId = Kernel32.GetCurrentThreadId();
        _hookProc = HookCallback;
        _winEventProc = WinEventCallback;
        _kbHook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, _hookProc, Kernel32.GetModuleHandle(null), 0);
        if (_kbHook == IntPtr.Zero) _log.LogError("SetWindowsHookEx failed: {Err}", Marshal.GetLastWin32Error());
        _winEventHook = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
        UpdateForeground(User32.GetForegroundWindow());
        _ready.Set();
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
        if (_kbHook != IntPtr.Zero) User32.UnhookWindowsHookEx(_kbHook);
        if (_winEventHook != IntPtr.Zero) User32.UnhookWinEvent(_winEventHook);
        _kbHook = IntPtr.Zero;
    }

    private unsafe IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = (User32.KBDLLHOOKSTRUCT*)lParam;
            bool ours = k->dwExtraInfo == InjectMarker;
            if (!ours && KeyFilter is { } filter)
            {
                try
                {
                    bool injected = (k->flags & User32.LLKHF_INJECTED) != 0;
                    bool up = (k->flags & User32.LLKHF_UP) != 0;
                    if (filter((ushort)k->vkCode, (ushort)k->scanCode, up, injected)) return 1;
                }
                catch (Exception e) { _log.LogError(e, "key filter failed"); }
            }
        }
        return User32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void WinEventCallback(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == User32.EVENT_SYSTEM_FOREGROUND) UpdateForeground(hwnd);
    }

    private void UpdateForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        var title = new StringBuilder(256);
        User32.GetWindowText(hwnd, title, title.Capacity);
        var info = new ForegroundInfo(pid, ProcessName(pid), title.ToString());
        if (info == _foreground) return;
        _foreground = info;
        if (_pinned is not null) return;
        ForegroundChanged?.Invoke(info);
    }

    // Short-lived cache: Windows reuses pids, so a stale entry could match the wrong app profile.
    private static readonly Dictionary<uint, (string name, long at)> NameCache = [];
    private const long NameCacheMs = 5_000;

    public static string ProcessName(uint pid)
    {
        lock (NameCache)
            if (NameCache.TryGetValue(pid, out var cached) && Environment.TickCount64 - cached.at < NameCacheMs) return cached.name;
        string name = "";
        var h = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (Kernel32.QueryFullProcessImageName(h, 0, sb, ref size)) name = Path.GetFileName(sb.ToString());
            Kernel32.CloseHandle(h);
        }
        lock (NameCache)
        {
            if (NameCache.Count > 512) NameCache.Clear();
            if (name.Length > 0) NameCache[pid] = (name, Environment.TickCount64);
        }
        return name;
    }

    public void Dispose()
    {
        if (_threadId != 0) User32.PostThreadMessage(_threadId, User32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
    }
}
