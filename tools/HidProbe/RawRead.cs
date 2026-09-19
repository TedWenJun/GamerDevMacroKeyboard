using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// --raw [seconds]: print every input report from each readable collection of the pad, with a timestamp,
/// including reports that repeat the previous one (the Hub only sees changes).
/// </summary>
static class RawRead
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr overlapped);

    public static void Run(string[] args)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 60;
        var clock = Stopwatch.StartNew();
        var gate = new object();
        foreach (var path in Hid.Enumerate())
        {
            if (!path.Contains("vid_b6a4&pid_410", StringComparison.OrdinalIgnoreCase)) continue;
            var h = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
            if (h.IsInvalid) continue;
            if (!Hid.HidD_GetPreparsedData(h, out var pp)) continue;
            Hid.HidP_GetCaps(pp, out var caps);
            Hid.HidD_FreePreparsedData(pp);
            if (caps.InputReportByteLength == 0) continue;
            string tag = $"{caps.UsagePage:X4}:{caps.Usage:X4}";
            Console.WriteLine($"reading {tag} len={caps.InputReportByteLength}");
            new Thread(() =>
            {
                var buf = new byte[caps.InputReportByteLength];
                while (ReadFile(h, buf, buf.Length, out int n, IntPtr.Zero))
                {
                    int end = n;
                    while (end > 1 && buf[end - 1] == 0) end--;
                    lock (gate) Console.WriteLine($"{clock.Elapsed.TotalSeconds,9:F3} {tag} {Convert.ToHexString(buf, 0, end)}");
                }
            }) { IsBackground = true }.Start();
        }
        Thread.Sleep(seconds * 1000);
    }
}
