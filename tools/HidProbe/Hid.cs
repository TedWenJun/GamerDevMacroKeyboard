using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

static class Hid
{
    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sec, uint c, uint f, IntPtr t);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder b, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetManufacturerString(SafeFileHandle h, StringBuilder b, int len);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("hid.dll")] public static extern int HidP_GetButtonCaps(int rt, [Out] HIDP_BUTTON_CAPS[] c, ref ushort len, IntPtr pp);
    [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int rt, [Out] HIDP_VALUE_CAPS[] c, ref ushort len, IntPtr pp);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS { public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved; public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices; }
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_BUTTON_CAPS { [FieldOffset(0)] public ushort UsagePage; [FieldOffset(2)] public byte ReportID; [FieldOffset(3)] public byte IsAlias; [FieldOffset(12)] public byte IsRange; [FieldOffset(56)] public ushort UsageMin; [FieldOffset(58)] public ushort UsageMax; }
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_VALUE_CAPS { [FieldOffset(0)] public ushort UsagePage; [FieldOffset(2)] public byte ReportID; [FieldOffset(12)] public byte IsRange; [FieldOffset(18)] public ushort BitSize; [FieldOffset(20)] public ushort ReportCount; [FieldOffset(36)] public int LogicalMin; [FieldOffset(40)] public int LogicalMax; [FieldOffset(56)] public ushort UsageMin; [FieldOffset(58)] public ushort UsageMax; }

    public static IEnumerable<string> Enumerate()
    {
        HidD_GetHidGuid(out var g);
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var list = new List<string>();
        var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int req, IntPtr.Zero);
            var buf = Marshal.AllocHGlobal(req);
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out _, IntPtr.Zero))
                list.Add(Marshal.PtrToStringUni(buf + 4)!);
            Marshal.FreeHGlobal(buf);
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }
}
