using System.Runtime.InteropServices;
using MacroHub.Core;
using MacroHub.Native;
using Microsoft.Win32.SafeHandles;

namespace MacroHub.Input;

/// <summary>
/// Writes the pad's backlight settings over its vendor feature channel (usage page 0xFF01, report id 6).
/// Frame layout: docs/device-protocol.md.
/// Only the lighting command (0x09), its apply (0x02) and the read-only status poll (0x81) are ever sent - the key remapping table lives behind a
/// different command (0x04) on the same channel and is never touched here.
/// </summary>
public sealed class PadLighting(ILogger log, Func<IReadOnlyList<string>> match)
{
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);

    private const int FrameLength = 41;
    private const byte ReportId = 0x06;
    private const byte CmdWrite = 0x09;
    private const byte CmdApply = 0x02;
    private const byte CmdStatus = 0x81;   // read-only status poll; the reply carries the battery
    private const int BlockSize = 0x19;     // 25 parameter bytes per mode; mode 0x80+N lives at N * 0x19

    private readonly object _gate = new();
    private byte _seq;
    private LightingSpec? _applied;

    /// <summary>The state the device was last given, so the UI can show what is actually on the pad.</summary>
    public LightingSpec? Applied { get { lock (_gate) return _applied; } }

    /// <summary>Feature collection of the pad (cable or 2.4G receiver alike), or null when it is not connected.</summary>
    private string? FindChannel()
    {
        foreach (var path in HidApi.EnumerateInterfaces())
        {
            if (!match().Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
            if (HidApi.GetCaps(path) is { } caps && caps.UsagePage == 0xFF01 && caps.FeatureReportByteLength == FrameLength)
            {
                return path;
            }
        }
        return null;
    }

    public bool Available => FindChannel() != null;

    /// <summary>Send a lighting state to the pad. Returns false when the pad is not reachable.</summary>
    public bool Apply(LightingSpec spec, bool force = false)
    {
        lock (_gate)
        {
            if (!force && _applied is { } previous && previous.SameAs(spec)) return true;

            var path = FindChannel();
            if (path == null)
            {
                log.LogDebug("lighting: pad not connected");
                return false;
            }

            using var device = Kernel32.CreateFile(path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
                Kernel32.FILE_SHARE_RW, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
            if (device.IsInvalid)
            {
                log.LogWarning("lighting: cannot open {Path} ({Error})", path, Marshal.GetLastWin32Error());
                return false;
            }

            int mode = Math.Clamp(spec.Mode, 1, LightingSpec.ModeCount);

            // 1) select the mode
            var select = new byte[BlockSize];
            select[0] = (byte)(0x80 + mode);
            select[1] = 0xFF;
            select[2] = 0x01;
            if (!Send(device, CmdWrite, 0x00, 0x0000, select)) return false;

            // 2) its parameters, unless the mode is "off", which has no block
            if (mode != LightingSpec.ModeOff)
            {
                var (r, g, b) = spec.Rgb();
                var block = new byte[BlockSize];
                block[0] = (byte)Math.Clamp(spec.Speed, 0, 5);
                block[1] = (byte)Math.Clamp(spec.Direction, 0, 1);
                block[2] = LightingSpec.BrightnessByte(spec.Brightness);   // 0..5, 0 = brightest
                block[3] = 0x7F;                                                // every colour slot enabled
                for (int i = 0; i < 7; i++)
                {
                    // Every slot gets the same colour: which slot a mode lights from varies, and the Hub exposes
                    // a single colour rather than a palette.
                    block[4 + i * 3 + 0] = r;
                    block[4 + i * 3 + 1] = g;
                    block[4 + i * 3 + 2] = b;
                }
                if (!Send(device, CmdWrite, 0x00, (ushort)(mode * BlockSize), block)) return false;
            }

            // 3) apply; its two bytes change between identical configurations,
            //    so they carry no checksum and zeroes are accepted.
            if (!Send(device, CmdApply, 0x01, 0x0000, [0x00, 0x00])) return false;

            _applied = spec.Clone();
            log.LogInformation("lighting: mode={Mode} brightness={Brightness} speed={Speed} direction={Direction} colour={Colour}",
                spec.Mode, spec.Brightness, spec.Speed, spec.Direction, spec.Color);
            return true;
        }
    }

    /// <summary>
    /// Ask the pad for its status (read-only command 0x81) and read the battery from the reply.
    /// Null when the pad is not reachable or the reply does not match the request.
    /// </summary>
    public PadBattery? QueryBattery()
    {
        lock (_gate)
        {
            var path = FindChannel();
            if (path == null) return null;
            using var device = Kernel32.CreateFile(path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
                Kernel32.FILE_SHARE_RW, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
            if (device.IsInvalid) return null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                byte seq = _seq;
                if (!Send(device, CmdStatus, 0x00, 0x0000, new byte[3])) return null;
                Thread.Sleep(30); // over 2.4G the reply has to come back from the pad
                var reply = new byte[FrameLength];
                reply[0] = ReportId;
                if (!HidD_GetFeature(device, reply, reply.Length)) return null;
                // The reply echoes the command and sequence number; anything else is a stale frame.
                if (reply[3] == CmdStatus && reply[4] == seq) return PadBattery.Parse(reply.AsSpan(9, 3));
            }
            return null;
        }
    }

    private bool Send(SafeFileHandle device, byte command, byte flag, ushort address, byte[] data)
    {
        var frame = new byte[FrameLength];
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

        if (!HidD_SetFeature(device, frame, frame.Length))
        {
            log.LogWarning("lighting: SetFeature cmd=0x{Command:X2} failed ({Error})", command, Marshal.GetLastWin32Error());
            return false;
        }
        Thread.Sleep(8); // the firmware drops frames that arrive back to back
        return true;
    }
}
