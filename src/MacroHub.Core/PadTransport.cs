namespace MacroHub.Core;

/// <summary>
/// How the pad is attached, read from the HID device path Windows gives its collections.
/// The W909 answers as PID 4100 on its cable and PID 4101 through its 2.4G receiver; over Bluetooth the path carries
/// the Bluetooth HID service instead of a plain USB VID/PID.
/// </summary>
public static class PadTransport
{
    public const string Usb = "usb";
    public const string Receiver = "2.4g";
    public const string Bluetooth = "bluetooth";
    public const string Unknown = "unknown";

    /// <summary>Match substrings that cover the W909 in every mode (see <see cref="Of"/>).</summary>
    public static readonly string[] W909Match =
    [
        "VID_B6A4&PID_4100",   // cable
        "VID_B6A4&PID_4101",   // 2.4G receiver
        "VID&0002B6A4",        // Bluetooth Classic: ...{00001124-...}_vid&0002b6a4_pid&...
        "VID&02B6A4",          // Bluetooth LE: ...{00001812-...}_dev_vid&02b6a4_pid&...
    ];

    public static string Of(string path)
    {
        if (path.Contains("{00001124-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("{00001812-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("vid&0002", StringComparison.OrdinalIgnoreCase)
            || path.Contains("_dev_vid&", StringComparison.OrdinalIgnoreCase))
            return Bluetooth;
        if (path.Contains("pid_4101", StringComparison.OrdinalIgnoreCase)) return Receiver;
        if (path.Contains("pid_4100", StringComparison.OrdinalIgnoreCase)) return Usb;
        return Unknown;
    }

    /// <summary>Transport of a set of open collections; empty when nothing is open.</summary>
    public static string Of(IEnumerable<string> paths)
    {
        var kinds = paths.Select(Of).Distinct().ToList();
        return kinds.Count switch
        {
            0 => "",
            1 => kinds[0],
            // Cable and receiver at once (e.g. charging while on 2.4G): the cable carries the input.
            _ => kinds.Contains(Usb) ? Usb : kinds[0],
        };
    }

    public static string Label(string transport, bool chinese = false) => transport switch
    {
        Usb => "USB",
        Receiver => "2.4G",
        Bluetooth => chinese ? "蓝牙" : "Bluetooth",
        "" => "",
        _ => chinese ? "未知" : "unknown",
    };
}
