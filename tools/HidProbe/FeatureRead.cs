using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
static class FeatureRead
{
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] buf, int len);
    public static void Run()
    {
        foreach (var path in Hid.Enumerate())
        {
            if (!path.Contains("vid_b6a4&pid_4100&mi_02", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.Contains("col04"))
            {
                using var h = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
                var b = new byte[41]; b[0] = 6;
                bool ok = HidD_GetFeature(h, b, b.Length);
                Console.WriteLine($"GET_FEATURE rid6 ok={ok} err={Marshal.GetLastWin32Error()} : {BitConverter.ToString(b)}");
            }
            if (path.Contains("col03"))
            {
                using var h = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
                var b = new byte[25]; b[0] = 5;
                bool ok = HidD_GetInputReport(h, b, b.Length);
                Console.WriteLine($"GET_INPUT rid5 ok={ok} err={Marshal.GetLastWin32Error()} : {BitConverter.ToString(b)}");
            }
        }
    }
}
