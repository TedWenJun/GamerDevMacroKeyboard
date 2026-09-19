using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacroHub.Native;

internal static unsafe class User32
{
    public const int WH_KEYBOARD_LL = 13;
    public const uint WM_INPUT = 0x00FF, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_QUIT = 0x0012, WM_APP = 0x8000;
    public const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4, KEYEVENTF_SCANCODE = 8;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10,
        MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_XDOWN = 0x80, MOUSEEVENTF_XUP = 0x100,
        MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_HWHEEL = 0x1000;
    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    public const uint RIDEV_INPUTSINK = 0x100;
    public const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007;
    public const uint RIM_TYPEMOUSE = 0, RIM_TYPEKEYBOARD = 1, RIM_TYPEHID = 2;

    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] r, uint n, uint size);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr h, uint cmd, byte* data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, char* data, ref uint size);
}

internal static class Kernel32
{
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
}

internal static class HidApi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr reserved; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder sb, int len);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    public static List<string> EnumerateInterfaces()
    {
        HidD_GetHidGuid(out var g);
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
        var list = new List<string>();
        var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int required, IntPtr.Zero);
            var buf = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, required, out _, IntPtr.Zero))
                    list.Add(Marshal.PtrToStringUni(buf + 4)!);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }

    public static HIDP_CAPS? GetCaps(string path)
    {
        using var h = Kernel32.CreateFile(path, 0, Kernel32.FILE_SHARE_RW, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid || !HidD_GetPreparsedData(h, out var pp)) return null;
        try { HidP_GetCaps(pp, out var caps); return caps; }
        finally { HidD_FreePreparsedData(pp); }
    }
}
