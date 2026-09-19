namespace MacroHub.Core;

/// <summary>
/// Battery state from the pad's status reply (read-only feature command 0x81, see docs/device-protocol.md).
/// Reply data, three bytes: [0] link (0 = cable, 1 = 2.4G receiver), [1] bit 7 = charging, bits 0..6 = percent, [2] 0.
/// Recorded: cable "00 E4 00" (charging, 100 %), 2.4G "01 53 00" (83 %).
/// </summary>
public sealed record PadBattery(int Percent, bool Charging, bool Wireless)
{
    public static PadBattery? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return null;
        int percent = data[1] & 0x7F;
        if (percent > 100) return null;
        return new PadBattery(percent, (data[1] & 0x80) != 0, data[0] != 0);
    }
}
