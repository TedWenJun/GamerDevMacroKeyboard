using System.Diagnostics;
using System.Runtime.InteropServices;

// InputRecorder: passive recorder (never blocks input).
//  - Full detail for the macro pad (VID_B6A4): raw keyboard / mouse / consumer / vendor reports.
//  - For every other keyboard only timing statistics (no key identity) to learn whether WM_INPUT
//    of real hardware is already queued when the WH_KEYBOARD_LL callback runs.
unsafe
{
    int minutes = args.Length > 0 ? int.Parse(args[0]) : 60;
    const string Target = "VID_B6A4";
    var outPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "record.log"));
    var fw = new StreamWriter(outPath, append: true) { AutoFlush = true };
    void L(string s) { var line = $"{DateTime.Now:HH:mm:ss.fff} {s}"; lock (fw) fw.WriteLine(line); Console.WriteLine(line); }

    var devNames = new Dictionary<IntPtr, string>();
    string DevName(IntPtr h)
    {
        if (h == IntPtr.Zero) return "<injected>";
        if (devNames.TryGetValue(h, out var n)) return n;
        uint sz = 0; W.GetRawInputDeviceInfo(h, 0x20000007, null, ref sz);
        var buf = new char[sz + 1];
        fixed (char* p = buf) W.GetRawInputDeviceInfo(h, 0x20000007, p, ref sz);
        return devNames[h] = new string(buf).TrimEnd('\0');
    }

    var recent = new List<(IntPtr dev, ushort vk, bool up)>();
    int hookHwTotal = 0, hookMatched = 0;
    IntPtr hwnd = IntPtr.Zero;

    void HandleRaw(IntPtr l, bool fromHook)
    {
        uint sz = 0; W.GetRawInputData(l, 0x10000003, null, ref sz, (uint)sizeof(W.RAWINPUTHEADER));
        byte* buf = stackalloc byte[(int)sz];
        if (W.GetRawInputData(l, 0x10000003, buf, ref sz, (uint)sizeof(W.RAWINPUTHEADER)) == uint.MaxValue) return;
        var hdr = (W.RAWINPUTHEADER*)buf; byte* body = buf + sizeof(W.RAWINPUTHEADER);
        string name = DevName(hdr->hDevice);
        bool isPad = name.Contains(Target, StringComparison.OrdinalIgnoreCase);
        if (hdr->dwType == 1) // RIM_TYPEKEYBOARD
        {
            var kb = (W.RAWKEYBOARD*)body;
            bool up = (kb->Flags & 1) != 0;
            lock (recent) { recent.Add((hdr->hDevice, kb->VKey, up)); if (recent.Count > 64) recent.RemoveAt(0); }
            if (isPad) L($"PAD KBD   vk=0x{kb->VKey:X2} make=0x{kb->MakeCode:X2} flags={kb->Flags} {(up ? "UP" : "DOWN")} drainedInHook={fromHook} dev={name}");
        }
        else if (hdr->dwType == 0 && isPad)
        {
            var m = (W.RAWMOUSE*)body;
            L($"PAD MOUSE flags=0x{m->usFlags:X} btnFlags=0x{m->usButtonFlags:X} btnData={(short)m->usButtonData} dx={m->lLastX} dy={m->lLastY}");
        }
        else if (hdr->dwType == 2 && isPad) // RIM_TYPEHID
        {
            var h = (W.RAWHID*)body; int n = (int)(h->dwSizeHid * h->dwCount);
            L($"PAD HID   {Convert.ToHexString(new ReadOnlySpan<byte>(body + 8, n))} dev={name}");
        }
    }

    W.WndProc wp = (h, m, w, l) =>
    {
        if (m == 0x00FF) try { HandleRaw(l, false); } catch (Exception e) { L("ERR wndproc " + e); }
        return W.DefWindowProc(h, m, w, l);
    };
    W.HookProc hp = (code, w, l) =>
    {
        if (code >= 0) try
        {
            var k = (W.KBDLLHOOKSTRUCT*)l;
            bool injected = (k->flags & 0x10) != 0;
            while (W.PeekMessage(out var pm, hwnd, 0x00FF, 0x00FF, 1)) { HandleRaw(pm.lParam, true); W.DefWindowProc(hwnd, pm.message, pm.wParam, pm.lParam); }
            bool up = (k->flags & 0x80) != 0;
            bool matched = false; IntPtr dev = IntPtr.Zero;
            lock (recent)
                for (int i = 0; i < recent.Count; i++)
                    if (recent[i].vk == k->vkCode && recent[i].up == up) { matched = true; dev = recent[i].dev; recent.RemoveAt(i); break; }
            if (!injected) { hookHwTotal++; if (matched) hookMatched++; }
            if (matched && DevName(dev).Contains(Target, StringComparison.OrdinalIgnoreCase))
                L($"PAD HOOK  vk=0x{k->vkCode:X2} scan=0x{k->scanCode:X2} {(up ? "UP" : "DOWN")} -> raw available at hook time");
            else if (!injected && !matched)
                L("HOOK-FIRST hw key, raw not yet available [identity not logged]");
        } catch (Exception e) { L("ERR hook " + e); }
        return W.CallNextHookEx(IntPtr.Zero, code, w, l);
    };

    new Thread(() =>
    {
        foreach (var path in Hid.Enumerate())
            if (path.Contains("vid_b6a4&pid_4100&mi_02&col03", StringComparison.OrdinalIgnoreCase))
            {
                var fh = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
                var b = new byte[64];
                L("vendor FF00 reader started");
                try { while (W.ReadFile(fh, b, 64, out int n, IntPtr.Zero)) L($"PAD VENDOR {Convert.ToHexString(b, 0, n)}"); L($"vendor ReadFile failed {Marshal.GetLastWin32Error()}"); }
                catch (Exception e) { L($"vendor reader stopped: {e.Message}"); }
            }
    }) { IsBackground = true }.Start();

    Keep.Wp = wp; Keep.Hp = hp;
    new Thread(() =>
    {
        var wc = new W.WNDCLASSEX { cbSize = Marshal.SizeOf<W.WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wp), hInstance = W.GetModuleHandle(null), lpszClassName = "InputRecorderWnd" };
        W.RegisterClassEx(ref wc);
        hwnd = W.CreateWindowEx(0, "InputRecorderWnd", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        var rids = new W.RAWINPUTDEVICE[] {
            new() { usUsagePage = 1, usUsage = 6, dwFlags = 0x100, hwndTarget = hwnd },
            new() { usUsagePage = 1, usUsage = 2, dwFlags = 0x100, hwndTarget = hwnd },
            new() { usUsagePage = 0x0C, usUsage = 1, dwFlags = 0x100, hwndTarget = hwnd },
            new() { usUsagePage = 1, usUsage = 0x80, dwFlags = 0x100, hwndTarget = hwnd },
        };
        L($"recorder start ({minutes} min) register={W.RegisterRawInputDevices(rids, (uint)rids.Length, (uint)sizeof(W.RAWINPUTDEVICE))} log={outPath}");
        W.SetWindowsHookEx(13, hp, W.GetModuleHandle(null), 0);
        while (W.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { W.TranslateMessage(ref msg); W.DispatchMessage(ref msg); }
    }) { IsBackground = true }.Start();

    for (int i = 0; i < minutes; i++)
    {
        Thread.Sleep(60000);
        L($"STATS hwKeys={hookHwTotal} hwRawAvailableAtHook={hookMatched}");
    }
}

static class Keep { public static object? Wp, Hp; }

unsafe static class W
{
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte[] b, int n, out int read, IntPtr ov);
    public delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWMOUSE { public ushort usFlags; public ushort pad; public ushort usButtonFlags, usButtonData; public uint ulRawButtons; public int lLastX, lLastY; public uint ulExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWHID { public uint dwSizeHid, dwCount; }
    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? n);
    [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] r, uint n, uint sz);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr h, uint cmd, byte* data, ref uint size, uint hdr);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, char* data, ref uint size);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint r);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
}
