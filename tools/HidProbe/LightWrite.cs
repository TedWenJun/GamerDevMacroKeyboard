using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// Writes the W909's lighting configuration over the vendor feature channel (usage page 0xFF01, report id 6),
/// using the frame layout in docs/device-protocol.md.
///
///   dotnet run --project tools/HidProbe -- --light mode=1 bright=7 speed=0 color=#FF0000
///
/// mode  1..9 (9 = lights off)   bright 1..7   speed 0..5   color: 1-7 comma separated #RRGGBB
static class LightWrite
{
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);

    const int ReportLength = 41;
    const byte ReportId = 0x06;
    const int BlockSize = 0x19;     // 25 bytes of parameters per mode
    static byte _seq = 0x20;

    public static int Run(string[] args)
    {
        int mode = 1, speed = 4, direction = 0, braw = 0, mask = 0x7F;
        var colours = new List<(byte R, byte G, byte B)>();
        foreach (var arg in args.Skip(1))
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2) continue;
            switch (parts[0].ToLowerInvariant())
            {
                case "mode": mode = int.Parse(parts[1]); break;
                case "braw": braw = int.Parse(parts[1]); break;
                case "speed": speed = int.Parse(parts[1]); break;
                case "dir": direction = int.Parse(parts[1]); break;
                
                case "mask": mask = Convert.ToInt32(parts[1], 16); break;   // raw brightness byte, for probing
                case "color":
                case "colors":
                    foreach (var c in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var hex = c.TrimStart('#');
                        colours.Add((Convert.ToByte(hex.Substring(0, 2), 16),
                                     Convert.ToByte(hex.Substring(2, 2), 16),
                                     Convert.ToByte(hex.Substring(4, 2), 16)));
                    }
                    break;
            }
        }
        if (mode is < 1 or > 9 || braw is < 0 or > 5 || speed is < 0 or > 5)
        {
            Console.WriteLine("mode 1..9, braw 0..5, speed 0..5");
            return 2;
        }

        var path = Hid.Enumerate().FirstOrDefault(p =>
            p.Contains("vid_b6a4&pid_4100&mi_02", StringComparison.OrdinalIgnoreCase) && p.Contains("col04"));
        if (path == null) { Console.WriteLine("W909 vendor collection (col04) not found"); return 1; }

        using var device = Hid.CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
        if (device.IsInvalid) { Console.WriteLine($"open failed {Marshal.GetLastWin32Error()}"); return 1; }

        // 1) select the mode
        var select = new byte[BlockSize];
        select[0] = (byte)(0x80 + mode);
        select[1] = 0xFF;
        select[2] = 0x01;
        Send(device, 0x09, 0x00, 0x0000, select);

        // 2) its parameter block, unless the mode is "off" (which has none)
        if (mode != 9)
        {
            var block = new byte[BlockSize];
            block[0] = (byte)speed;                 // 0..5
            block[1] = (byte)direction;             // 0 or 1, only used by the flowing modes
            block[2] = (byte)braw;                  // brightness as the firmware stores it (0..5, 0 = brightest)
            block[3] = (byte)mask;                  // which colour slots are enabled, one bit each (0x7F = all)
            for (int i = 0; i < 7; i++)
            {
                // Fewer colours than slots: repeat them, because a mode may light from any slot and a black
                // slot simply means "no light" (writing only slot 1 turned the pad off).
                var (r, g, bl) = colours.Count > 0 ? colours[i % colours.Count] : ((byte)0xFF, (byte)0, (byte)0);
                block[4 + i * 3 + 0] = r;           // stored R, G, B
                block[4 + i * 3 + 1] = g;
                block[4 + i * 3 + 2] = bl;
            }
            Send(device, 0x09, 0x00, (ushort)(mode * BlockSize), block);
        }

        // 3) apply; its two bytes change between identical configurations, so zeroes are accepted
        Send(device, 0x02, 0x01, 0x0000, new byte[] { 0x00, 0x00 });
        Console.WriteLine($"sent: mode={mode} braw={braw} mask=0x{mask:X2} speed={speed} colours={colours.Count}");
        return 0;
    }

    static void Send(SafeFileHandle device, byte command, byte flag, ushort address, byte[] data)
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = 0x00;
        frame[2] = 0x01;
        frame[3] = command;
        frame[4] = _seq++;
        frame[5] = flag;
        frame[6] = (byte)(address & 0xFF);
        frame[7] = (byte)(address >> 8);
        frame[8] = (byte)data.Length;
        Array.Copy(data, 0, frame, 9, data.Length);

        bool ok = HidD_SetFeature(device, frame, frame.Length);
        Console.WriteLine($"  SET ok={ok} err={Marshal.GetLastWin32Error()} : {BitConverter.ToString(frame)}");
        Thread.Sleep(30);
    }
}
