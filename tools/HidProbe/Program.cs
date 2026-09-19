using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// HidProbe: enumerate HID collections of a VID/PID and dump HidP caps.
if (args.Length > 0 && args[0] == "--feature") { FeatureRead.Run(); return; }
if (args.Length > 0 && args[0] == "--raw") { RawRead.Run(args); return; }
if (args.Length > 0 && args[0] == "--light") { Environment.ExitCode = LightWrite.Run(args); return; }
string vidpid = args.Length > 0 ? args[0] : "vid_b6a4&pid_4100";
foreach (var path in Hid.Enumerate())
{
    if (!path.Contains(vidpid, StringComparison.OrdinalIgnoreCase)) continue;
    Console.WriteLine($"== {path}");
    using var h = Hid.CreateFile(path, 0, 3, 0, 3, 0, 0);
    if (h.IsInvalid) { Console.WriteLine($"   open(0) failed {Marshal.GetLastWin32Error()}"); continue; }
    var sb = new StringBuilder(256);
    if (Hid.HidD_GetProductString(h, sb, 256)) Console.WriteLine($"   product: {sb}");
    sb.Clear(); if (Hid.HidD_GetManufacturerString(h, sb, 256)) Console.WriteLine($"   manufacturer: {sb}");
    if (!Hid.HidD_GetPreparsedData(h, out var pp)) { Console.WriteLine("   no preparsed"); continue; }
    Hid.HidP_GetCaps(pp, out var caps);
    Console.WriteLine($"   UsagePage=0x{caps.UsagePage:X4} Usage=0x{caps.Usage:X4} InLen={caps.InputReportByteLength} OutLen={caps.OutputReportByteLength} FeatLen={caps.FeatureReportByteLength}");
    foreach (var (rt, name, nb, nv) in new[] { (0, "Input", caps.NumberInputButtonCaps, caps.NumberInputValueCaps), (1, "Output", caps.NumberOutputButtonCaps, caps.NumberOutputValueCaps), (2, "Feature", caps.NumberFeatureButtonCaps, caps.NumberFeatureValueCaps) })
    {
        if (nb > 0)
        {
            var bc = new Hid.HIDP_BUTTON_CAPS[nb]; ushort n = nb;
            Hid.HidP_GetButtonCaps(rt, bc, ref n, pp);
            foreach (var b in bc) Console.WriteLine($"   {name} Button RID={b.ReportID} page=0x{b.UsagePage:X4} usage=0x{b.UsageMin:X4}-0x{b.UsageMax:X4} range={b.IsRange}");
        }
        if (nv > 0)
        {
            var vc = new Hid.HIDP_VALUE_CAPS[nv]; ushort n = nv;
            Hid.HidP_GetValueCaps(rt, vc, ref n, pp);
            foreach (var v in vc) Console.WriteLine($"   {name} Value  RID={v.ReportID} page=0x{v.UsagePage:X4} usage=0x{v.UsageMin:X4}-0x{v.UsageMax:X4} bits={v.BitSize}x{v.ReportCount} log=[{v.LogicalMin},{v.LogicalMax}]");
        }
    }
    // try opening with read access (non-keyboard/mouse collections allow it)
    using var hr = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
    Console.WriteLine($"   open(RW): {(hr.IsInvalid ? "denied " + Marshal.GetLastWin32Error() : "OK")}");
    Hid.HidD_FreePreparsedData(pp);
}

