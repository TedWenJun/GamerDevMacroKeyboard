using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroHub.Core;
using MacroHub.Native;
using Microsoft.Win32.SafeHandles;

namespace MacroHub.Input;

/// <summary>
/// Reads the pad's non-exclusive HID collections directly (vendor bitmap FF00 and consumer control 0C/01).
/// Keyboard/mouse collections are opened exclusively by Windows and are not readable here.
/// Physical signals from this reader arrive before the low-level keyboard hook sees the keystroke.
/// </summary>
public sealed class PadHidReader : IDisposable
{
    public sealed record Collection(string Path, ushort UsagePage, ushort Usage, int InputLength);

    private readonly ILogger _log;
    private readonly Func<DeviceMap> _device;
    private readonly List<(SafeFileHandle handle, Thread thread)> _open = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _watch;

    public event Action<Signal, long>? SignalReceived;
    public event Action<bool>? ConnectionChanged;

    public IReadOnlyList<Collection> Collections { get; private set; } = [];
    public bool Connected => Collections.Count > 0;
    /// <summary>usb, 2.4g or bluetooth (see <see cref="PadTransport"/>); empty when not connected.</summary>
    public string Transport => PadTransport.Of(Collections.Select(c => c.Path));

    public PadHidReader(ILogger log, Func<DeviceMap> device)
    {
        _log = log;
        _device = device;
    }

    public void Start()
    {
        _watch = new Thread(WatchLoop) { IsBackground = true, Name = "PadHidWatch" };
        _watch.Start();
    }

    /// <summary>Force re-enumeration (e.g. after the device match list changed).</summary>
    public void Reconnect() { lock (_gate) CloseAll(); }

    public static List<Collection> Find(IEnumerable<string> match)
    {
        var result = new List<Collection>();
        foreach (var path in HidApi.EnumerateInterfaces())
        {
            if (!match.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
            var caps = HidApi.GetCaps(path);
            if (caps is { } c) result.Add(new Collection(path, c.UsagePage, c.Usage, c.InputReportByteLength));
        }
        return result;
    }

    private void WatchLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                bool need;
                lock (_gate) need = _open.Count == 0 || _open.Any(o => !o.thread.IsAlive);
                if (need) TryOpen();
            }
            catch (Exception e) { _log.LogWarning(e, "HID watch failed"); }
            _cts.Token.WaitHandle.WaitOne(1500);
        }
    }

    private void TryOpen()
    {
        lock (_gate)
        {
            bool wasConnected = Connected;
            CloseAll();
            var device = _device();
            var found = Find(device.Match);
            var readable = new List<Collection>();
            foreach (var col in found)
            {
                bool vendor = col.UsagePage >= 0xFF00 && col.InputLength > 0;
                bool consumer = col.UsagePage == 0x0C && col.Usage == 0x01;
                if (!vendor && !consumer) continue;
                var h = Kernel32.CreateFile(col.Path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE, Kernel32.FILE_SHARE_RW, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h.IsInvalid) { _log.LogWarning("cannot open {Path}: {Err}", col.Path, Marshal.GetLastWin32Error()); continue; }
                var t = new Thread(() => ReadLoop(h, col, vendor, device.VendorReportId)) { IsBackground = true, Name = $"PadHid {col.UsagePage:X4}" };
                _open.Add((h, t));
                readable.Add(col);
                t.Start();
            }
            Collections = readable;
            if (Connected != wasConnected)
            {
                _log.LogInformation(Connected ? "pad connected ({Count} collections)" : "pad disconnected", readable.Count);
                ConnectionChanged?.Invoke(Connected);
            }
        }
    }

    private void ReadLoop(SafeFileHandle h, Collection col, bool vendor, byte vendorReportId)
    {
        var decoder = new VendorBitmapDecoder { ReportId = vendorReportId };
        var buf = new byte[Math.Max(col.InputLength, 64)];
        int lastConsumer = 0;
        var fs = new FileStream(h, FileAccess.Read, 0, false);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = fs.Read(buf, 0, col.InputLength);
                if (n <= 0) break;
                long now = HubClock.Ms;
                var report = buf.AsSpan(0, n);
                if (vendor)
                {
                    lock (_turnGate)
                    {
                        FlushPendingReleases();
                        var held = TurnFrameCodes();
                        foreach (var s in decoder.Decode(report))
                        {
                            if (s.Phase == ControlPhase.Up && held.Contains(s.Code)) _pendingUps.Add(s);
                            else SignalReceived?.Invoke(s, now);
                        }
                        if (_pendingUps.Count > 0)
                        {
                            _pendingDecoder = decoder;
                            _pendingAt = now;
                            PendingTimer.Change(TurnFrameMs, Timeout.Infinite);
                        }
                    }
                }
                else if (n >= 3)
                {
                    // consumer report: [id][usage lo][usage hi]; 0 = release of the previous usage
                    int usage = report[1] | report[2] << 8;
                    if (usage != lastConsumer)
                    {
                        if (usage != 0) CancelPendingReleases();
                        if (lastConsumer != 0) SignalReceived?.Invoke(new Signal(SignalSource.Consumer, lastConsumer, ControlPhase.Up), now);
                        if (usage != 0) SignalReceived?.Invoke(new Signal(SignalSource.Consumer, usage, ControlPhase.Down), now);
                        lastConsumer = usage;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (!_cts.IsCancellationRequested) _log.LogInformation("HID read ended on {Page:X4}: {Msg}", col.UsagePage, e.Message);
        }
        catch (Exception e) { _log.LogError(e, "HID reader crashed"); }
    }

    // ───────────── turn frames ─────────────
    // Every knob step is sent as an all-zero vendor report followed ~1 ms later by the consumer volume report, so a
    // knob held down while it turns looks released on the first step, and its real release (another all-zero report)
    // then changes nothing. For the knob press, a release is therefore held back briefly: a turn right after it
    // means it was only a turn frame, and the button still counts as pressed.

    private const int TurnFrameMs = 10;
    private readonly object _turnGate = new();
    private readonly List<Signal> _pendingUps = [];
    private VendorBitmapDecoder? _pendingDecoder;
    private long _pendingAt;
    private Timer? _pendingTimer;
    private Timer PendingTimer => _pendingTimer ??= new Timer(_ => { lock (_turnGate) FlushPendingReleases(); });

    /// <summary>Vendor codes of knob presses: the only bits whose releases a turn frame can fake.</summary>
    private HashSet<int> TurnFrameCodes()
    {
        var codes = new HashSet<int>();
        foreach (var c in _device().Controls)
        {
            if (c.Kind != "knob" || c.Part != "press") continue;
            foreach (var sig in c.Signatures)
                if (sig.StartsWith("vendor:") && int.TryParse(sig.AsSpan(7), out int code)) codes.Add(code);
        }
        return codes;
    }

    /// <summary>No turn followed: the releases were real.</summary>
    private void FlushPendingReleases()
    {
        if (_pendingUps.Count == 0) return;
        foreach (var s in _pendingUps) SignalReceived?.Invoke(s, _pendingAt);
        _pendingUps.Clear();
        _pendingDecoder = null;
    }

    /// <summary>A turn followed: the releases were a turn frame, the buttons are still down.</summary>
    private void CancelPendingReleases()
    {
        lock (_turnGate)
        {
            if (_pendingUps.Count == 0) return;
            _pendingDecoder?.Hold(_pendingUps.Select(s => s.Code));
            _pendingUps.Clear();
            _pendingDecoder = null;
        }
    }

    private void CloseAll()
    {
        foreach (var (h, _) in _open)
        {
            try { Kernel32.CancelIoEx(h, IntPtr.Zero); h.Dispose(); } catch { }
        }
        _open.Clear();
        Collections = [];
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate) CloseAll();
        _pendingTimer?.Dispose();
    }
}
