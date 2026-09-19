using System.Runtime.InteropServices;
using MacroHub.Core;

namespace MacroHub.Audio;

/// <summary>
/// Undoes the Windows master-volume change the W909 knob causes natively. The knob sends HID consumer
/// Volume Increment/Decrement which the OS applies by itself (it never reaches the keyboard hook, so it cannot be
/// blocked). When a knob event is used for something else (UE time slider, block layer, ...) <see cref="Arm"/> is
/// called; any volume/mute change reported by the endpoint during the guard window is reverted to the baseline.
///
/// Baseline tracking: volume notifications outside a guard become the baseline only once they are older than
/// <see cref="SettleMs"/> — the OS may apply the knob's change slightly before the Hub reads the HID report.
/// </summary>
public sealed class VolumeGuard : IDisposable
{
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Guid _ourContext = Guid.NewGuid();
    private IAudioEndpointVolume? _endpoint;
    private Callback? _callback;
    private long _lastInitMs = long.MinValue;

    private float _baseline;
    private bool _baselineMute;
    private (float level, bool mute, long t)? _candidate;
    private long _guardUntil = long.MinValue;
    private int _restores;

    /// <summary>How long after the last knob event changes are reverted.</summary>
    public long WindowMs { get; set; } = 400;
    public long SettleMs { get; set; } = 60;

    public bool Available => _endpoint is not null;
    public int Restores => _restores;

    public VolumeGuard(ILogger log) => _log = log;

    public void Start() => EnsureEndpoint();

    public void Arm()
    {
        long now = HubClock.Ms;
        EnsureEndpoint();
        lock (_gate)
        {
            if (now > _guardUntil)
            {
                // entering a new guard: promote a settled candidate, discard one that may be the knob's own change
                if (_candidate is { } c && now - c.t >= SettleMs) (_baseline, _baselineMute) = (c.level, c.mute);
                _candidate = null;
            }
            _guardUntil = now + WindowMs;
        }
    }

    public object Status()
    {
        float level = 0; bool mute = false;
        try { if (_endpoint is { } ep) { ep.GetMasterVolumeLevelScalar(out level); ep.GetMute(out mute); } } catch { }
        lock (_gate)
            return new { available = Available, level = Math.Round(level, 4), mute, baseline = Math.Round(_baseline, 4), baselineMute = _baselineMute,
                guarding = HubClock.Ms <= _guardUntil, restores = _restores };
    }

    private void EnsureEndpoint()
    {
        long now = HubClock.Ms;
        lock (_gate)
        {
            // re-acquire periodically when idle so a changed default output device is picked up
            if (_endpoint is not null && (now - _lastInitMs < 10_000 || now <= _guardUntil)) return;
            _lastInitMs = now;
            try
            {
                Release();
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device));
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj));
                _endpoint = (IAudioEndpointVolume)obj;
                _callback = new Callback(this);
                _endpoint.RegisterControlChangeNotify(_callback);
                _endpoint.GetMasterVolumeLevelScalar(out _baseline);
                _endpoint.GetMute(out _baselineMute);
            }
            catch (Exception e)
            {
                _log.LogWarning("volume guard unavailable: {Message}", e.Message);
                _endpoint = null;
            }
        }
    }

    private void OnNotify(Guid context, bool muted, float level)
    {
        if (context == _ourContext) return;
        long now = HubClock.Ms;
        bool restore;
        lock (_gate)
        {
            restore = now <= _guardUntil && (Math.Abs(level - _baseline) > 0.0005f || muted != _baselineMute);
            if (!restore) _candidate = (level, muted, now);
        }
        if (!restore) return;
        // Endpoint callbacks must not call back into the endpoint synchronously; restore on the thread pool.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                float target; bool targetMute;
                lock (_gate) (target, targetMute) = (_baseline, _baselineMute);
                var ctx = _ourContext;
                _endpoint?.SetMasterVolumeLevelScalar(target, ref ctx);
                _endpoint?.SetMute(targetMute, ref ctx);
                Interlocked.Increment(ref _restores);
            }
            catch (Exception e) { _log.LogWarning("volume restore failed: {Message}", e.Message); }
        });
    }

    private void Release()
    {
        try { if (_endpoint is not null && _callback is not null) _endpoint.UnregisterControlChangeNotify(_callback); } catch { }
        _endpoint = null;
        _callback = null;
    }

    public void Dispose()
    {
        lock (_gate) Release();
    }

    [ComVisible(true)]
    internal sealed class Callback(VolumeGuard owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA { GUID guidEventContext; BOOL bMuted; float fMasterVolume; ... }
            var context = Marshal.PtrToStructure<Guid>(data);
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            owner.OnNotify(context, muted, level);
            return 0;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }
}
