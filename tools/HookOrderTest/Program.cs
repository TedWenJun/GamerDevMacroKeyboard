using System.Diagnostics;
using System.Runtime.InteropServices;
// Experiment: ordering of WM_INPUT vs WH_KEYBOARD_LL for the same key event, and whether
// blocking in the LL hook suppresses WM_INPUT. Uses SendInput(F13/F14) as stimulus.
unsafe
{
    var sw = Stopwatch.StartNew();
    var log = new List<string>();
    void L(string s) { lock (log) log.Add($"{sw.Elapsed.TotalMilliseconds,8:F3} {s}"); }
    IntPtr hwnd = IntPtr.Zero;
    bool blockF14 = true;
    W.WndProc wp = (h, m, w, l) =>
    {
        if (m == 0x00FF)
        {
            uint sz = 0; W.GetRawInputData(l, 0x10000003, null, ref sz, (uint)sizeof(W.RAWINPUTHEADER));
            var buf = stackalloc byte[(int)sz];
            W.GetRawInputData(l, 0x10000003, buf, ref sz, (uint)sizeof(W.RAWINPUTHEADER));
            var hdr = (W.RAWINPUTHEADER*)buf; var kb = (W.RAWKEYBOARD*)(buf + sizeof(W.RAWINPUTHEADER));
            L($"WM_INPUT  vk=0x{kb->VKey:X2} flags={kb->Flags} dev=0x{hdr->hDevice:X}");
            return 0;
        }
        return W.DefWindowProc(h, m, w, l);
    };
    W.HookProc hp = (code, w, l) =>
    {
        var k = (W.KBDLLHOOKSTRUCT*)l;
        W.MSG pm;
        bool pending = W.PeekMessage(out pm, hwnd, 0x00FF, 0x00FF, 0) ;
        L($"LL_HOOK   vk=0x{k->vkCode:X2} msg=0x{w:X} injected={(k->flags & 0x10) != 0} WM_INPUT_pending={pending}");
        if (blockF14 && k->vkCode == 0x7D) { L("          -> blocked"); return 1; }
        return W.CallNextHookEx(IntPtr.Zero, code, w, l);
    };
    var t = new Thread(() =>
    {
        var wc = new W.WNDCLASSEX { cbSize = sizeof(W.WNDCLASSEX), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wp), hInstance = W.GetModuleHandle(null), lpszClassName = "HookOrderTestWnd" };
        W.RegisterClassEx(ref wc);
        hwnd = W.CreateWindowEx(0, "HookOrderTestWnd", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        var rid = new W.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = 0x100, hwndTarget = hwnd };
        L($"RegisterRawInputDevices={W.RegisterRawInputDevices(ref rid, 1, (uint)sizeof(W.RAWINPUTDEVICE))}");
        var hook = W.SetWindowsHookEx(13, hp, W.GetModuleHandle(null), 0);
        L($"hook=0x{hook:X}");
        while (W.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { W.TranslateMessage(ref msg); W.DispatchMessage(ref msg); }
    }) { IsBackground = true };
    t.Start();
    Thread.Sleep(500);
    foreach (ushort vk in new ushort[] { 0x7C, 0x7D }) // F13 (pass), F14 (blocked)
    {
        var inp = new W.INPUT[2];
        inp[0].type = 1; inp[0].ki.wVk = vk;
        inp[1].type = 1; inp[1].ki.wVk = vk; inp[1].ki.dwFlags = 2;
        L($"SendInput vk=0x{vk:X2}");
        W.SendInput(2, inp, sizeof(W.INPUT));
        Thread.Sleep(300);
    }
    lock (log) foreach (var s in log) Console.WriteLine(s);
}

unsafe static class W
{
    public delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? n);
    [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(ref RAWINPUTDEVICE r, uint n, uint sz);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr h, uint cmd, byte* data, ref uint size, uint hdr);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint r);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int sz);
}
