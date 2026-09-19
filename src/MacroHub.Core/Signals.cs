namespace MacroHub.Core;

public enum ControlPhase { Down, Up }

/// <summary>Where a hardware signal was observed.</summary>
public enum SignalSource
{
    /// <summary>Vendor HID input collection (bitmap of physical switches).</summary>
    Vendor,
    /// <summary>Keyboard key (VK/scan code) seen by the hook or Raw Input.</summary>
    Key,
    /// <summary>HID consumer page usage (media keys, volume knob).</summary>
    Consumer,
    /// <summary>Mouse button or wheel produced by the pad.</summary>
    Mouse,
}

/// <summary>
/// A normalized hardware signal. A "signature" is the identity part of a signal (everything but the phase)
/// and is what gets mapped to a logical control in <see cref="DeviceMap"/>.
/// </summary>
public readonly record struct Signal(SignalSource Source, int Code, ControlPhase Phase)
{
    public string Signature => SignatureOf(Source, Code);

    public static string SignatureOf(SignalSource source, int code) => source switch
    {
        SignalSource.Vendor => $"vendor:{code}",
        SignalSource.Key => $"key:{code:X2}",
        SignalSource.Consumer => $"consumer:{code:X3}",
        SignalSource.Mouse => $"mouse:{code}",
        _ => $"?:{code}",
    };

    public override string ToString() => $"{Signature} {Phase}";
}

/// <summary>
/// Decodes the W909 vendor report (report id 5): bytes after the id form a bitmap of pressed switches.
/// Each set bit index becomes a Vendor signal code, so a report diff yields Down/Up signals.
/// </summary>
public sealed class VendorBitmapDecoder
{
    private byte[] _last = [];

    public byte ReportId { get; init; } = 5;

    public List<Signal> Decode(ReadOnlySpan<byte> report)
    {
        var result = new List<Signal>();
        if (report.Length == 0 || report[0] != ReportId)
            return result;
        var payload = report[1..];
        int len = Math.Max(payload.Length, _last.Length);
        for (int i = 0; i < len; i++)
        {
            byte now = i < payload.Length ? payload[i] : (byte)0;
            byte before = i < _last.Length ? _last[i] : (byte)0;
            byte changed = (byte)(now ^ before);
            for (int bit = 0; changed != 0 && bit < 8; bit++)
            {
                if ((changed & (1 << bit)) == 0) continue;
                bool down = (now & (1 << bit)) != 0;
                result.Add(new Signal(SignalSource.Vendor, i * 8 + bit, down ? ControlPhase.Down : ControlPhase.Up));
            }
        }
        _last = payload.ToArray();
        return result;
    }

    public void Reset() => _last = [];

    /// <summary>Treat these codes as still pressed, so the next report that clears them yields their Up again.</summary>
    public void Hold(IEnumerable<int> codes)
    {
        foreach (int code in codes)
        {
            int i = code / 8;
            if (i >= _last.Length) Array.Resize(ref _last, i + 1);
            _last[i] |= (byte)(1 << (code % 8));
        }
    }
}
