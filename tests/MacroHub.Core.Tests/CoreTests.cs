using MacroHub.Core;
using System.Text.Json.Nodes;
using Xunit;

namespace MacroHub.Core.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("Ctrl+Shift+S", new ushort[] { 0x11, 0x10 }, new ushort[] { 0x53 })]
    [InlineData("Alt+P", new ushort[] { 0x12 }, new ushort[] { 0x50 })]
    [InlineData("F13", new ushort[0], new ushort[] { 0x7C })]
    [InlineData("Ctrl++", new ushort[] { 0x11 }, new ushort[] { 0xBB })]
    [InlineData("Shift", new ushort[0], new ushort[] { 0x10 })]
    [InlineData("VolumeUp", new ushort[0], new ushort[] { 0xAF })]
    public void ParsesChords(string text, ushort[] mods, ushort[] keys)
    {
        Assert.True(KeyChord.TryParse(text, out var chord, out var err), err);
        Assert.Equal(mods, chord.Modifiers);
        Assert.Equal(keys, chord.Keys);
    }

    [Fact]
    public void ParsesSequence()
    {
        Assert.True(KeyChord.TryParseSequence("Ctrl+K, Ctrl+C", out var chords, out _));
        Assert.Equal(2, chords.Count);
        Assert.Equal("Ctrl+C", chords[1].ToString());
    }

    [Fact]
    public void RejectsUnknownKey()
    {
        Assert.False(KeyChord.TryParseSequence("Ctrl+Banana", out _, out var err));
        Assert.Contains("Banana", err);
    }
}

public class VendorBitmapDecoderTests
{
    private static byte[] Report(params (int index, byte value)[] set)
    {
        var r = new byte[25];
        r[0] = 5;
        foreach (var (i, v) in set) r[1 + i] = v;
        return r;
    }

    [Fact]
    public void DecodesPressAndRelease()
    {
        var d = new VendorBitmapDecoder();
        // Recorded on the W909: key "0" → payload byte 3 = 0x02 → code 25
        var down = d.Decode(Report((3, 0x02)));
        Assert.Equal([new Signal(SignalSource.Vendor, 25, ControlPhase.Down)], down);
        var both = d.Decode(Report((3, 0x02), (4, 0x02)));
        Assert.Equal([new Signal(SignalSource.Vendor, 33, ControlPhase.Down)], both);
        var up = d.Decode(Report((4, 0x02)));
        Assert.Equal([new Signal(SignalSource.Vendor, 25, ControlPhase.Up)], up);
    }

    [Fact]
    public void HeldCodeReleasesOnTheNextClearingReport()
    {
        // Recorded on the W909: knob press = byte 5 bit 2 (code 42); each turn step first sends an all-zero report.
        var d = new VendorBitmapDecoder();
        d.Decode(Report((5, 0x04)));
        Assert.Equal([new Signal(SignalSource.Vendor, 42, ControlPhase.Up)], d.Decode(Report())); // turn frame
        d.Hold([42]);
        Assert.Empty(d.Decode(Report((5, 0x04)))); // the firmware re-sending the press while held
        Assert.Equal([new Signal(SignalSource.Vendor, 42, ControlPhase.Up)], d.Decode(Report())); // real release
    }

    [Fact]
    public void IgnoresOtherReportIds() =>
        Assert.Empty(new VendorBitmapDecoder().Decode(new byte[] { 6, 0xFF, 0xFF }));
}

public class ConfigAndRouterTests
{
    private static HubConfig Defaults() => HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));

    [Fact]
    public void CableOnlyMatchGainsTheReceiver()
    {
        var c = new HubConfig();
        c.Device.Match = ["VID_B6A4&PID_4100"];
        c.Normalize().Normalize();
        Assert.Equal(PadTransport.W909Match, c.Device.Match);
    }

    [Fact]
    public void EnglishDefaultsDifferOnlyInDisplayNames()
    {
        // hub.json seeds Chinese installs and hub.en.json the rest; everything but names must stay identical.
        static JsonNode Strip(JsonNode node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var key in new[] { "name", "label", "category" }) o.Remove(key);
                    foreach (var child in o.Select(p => p.Value).OfType<JsonNode>().ToList()) Strip(child);
                    break;
                case JsonArray a:
                    foreach (var child in a.OfType<JsonNode>()) Strip(child);
                    break;
            }
            return node;
        }
        var zh = Strip(JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub.json")))!);
        var en = Strip(JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub.en.json")))!);
        Assert.True(JsonNode.DeepEquals(zh, en));
        Assert.Empty(HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.en.json")).Validate());
    }

    [Fact]
    public void DefaultConfigIsValid()
    {
        var c = Defaults();
        Assert.Empty(c.Validate());
        Assert.Equal(23, c.Device.Controls.Count);
    }

    [Fact]
    public void RoundTripsJson()
    {
        var c = Defaults();
        var path = Path.GetTempFileName();
        c.Save(path);
        var again = HubConfig.Load(path);
        Assert.Equal(c.Functions.Count, again.Functions.Count);
        Assert.IsType<KeysAction>(again.Functions.First(f => f.Id == "ue.play").Action);
        File.Delete(path);
    }

    [Fact]
    public void SignatureLookup()
    {
        var r = new Router(Defaults());
        Assert.Equal("K0", r.ControlForSignature("vendor:25"));
        Assert.Equal("KNOB_CW", r.ControlForSignature(Signal.SignatureOf(SignalSource.Consumer, 0xE9)));
        Assert.Equal(["KENTER", "JOY_PRESS"], r.ControlsForVk(0x0D));
        Assert.Empty(r.ControlsForVk(0x41)); // 'A' never comes from the pad
    }

    [Fact]
    public void RoutesThroughLayerAndAppOverride()
    {
        var r = new Router(Defaults());
        // no app → manual layer (stock) → passthrough
        var res = r.Route("K1", ControlPhase.Down, "notepad.exe");
        Assert.Equal("stock", res.Layer);
        Assert.False(res.Block);

        r.SetLayer(new LayerAction("set", "desktop"));
        res = r.Route("K7", ControlPhase.Down, "notepad.exe");
        Assert.Equal("edit.find", res.Function);
        Assert.Equal(new KeysAction("Ctrl+F"), res.Action);

        // VS Code overrides the find function
        res = r.Route("K7", ControlPhase.Down, "Code.exe");
        Assert.Equal(new KeysAction("Ctrl+Shift+F"), res.Action);

        // Unreal Editor forces its layer; its profile forwards raw controls (handled by the engine when the plugin is
        // connected), so the Router's own result is the fallback layer route and is not a function forward
        res = r.Route("K1", ControlPhase.Down, "UnrealEditor.exe");
        Assert.Equal("unreal", res.Layer);
        Assert.Equal("ue.play", res.Function);
        Assert.False(res.Forward);
        Assert.True(res.Block);
        Assert.Equal(AppProfile.ForwardControls, r.MatchApp("UnrealEditor.exe")!.Forward);
    }

    [Fact]
    public void WildcardProcessAndBaseLayerFallback()
    {
        var r = new Router(Defaults());
        var res = r.Route("K7", ControlPhase.Down, "MyGame-Win64-Shipping.exe");
        Assert.Equal("game", res.Layer);
        Assert.Equal("pass", res.Function); // K7 not mapped in game layer → falls back to first layer
        Assert.Equal("game.forward", r.Route("JOY_UP", ControlPhase.Down, "MyGame-Win64-Shipping.exe").Function);
    }

    [Fact]
    public void LayerCycling()
    {
        var r = new Router(Defaults());
        string? changed = null;
        r.LayerChanged += l => changed = l;
        r.SetLayer(new LayerAction("next"));
        Assert.Equal("desktop", changed);
        r.SetLayer(new LayerAction("prev"));
        r.SetLayer(new LayerAction("prev"));
        Assert.Equal("diag", r.ManualLayer);
    }

    [Fact]
    public void ValidationFindsProblems()
    {
        var c = Defaults();
        c.Layers[0].Map["NOPE"] = "missing.fn";
        c.Functions.Add(new FunctionDef { Id = "bad", Action = new KeysAction("Ctrl+Nope") });
        var errors = c.Validate();
        Assert.Contains(errors, e => e.Contains("NOPE"));
        Assert.Contains(errors, e => e.Contains("missing.fn"));
        Assert.Contains(errors, e => e.Contains("Nope"));
    }
}

public class SuppressionCorrelatorTests
{
    [Fact]
    public void BlocksKeyArmedByPhysicalPress()
    {
        var c = new SuppressionCorrelator();
        c.Expect("K8", [0x68], block: true, now: 1000);
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x68, false, true, 1001, out var ctl));
        Assert.Equal("K8", ctl);
        // auto repeat while held
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x68, false, true, 1500, out _));
        // key up of held vk
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x68, true, true, 1600, out _));
        // afterwards the same key from the main keyboard passes
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 1700, out _));
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x68, true, true, 1701, out _));
    }

    [Fact]
    public void ExpectationExpires()
    {
        var c = new SuppressionCorrelator { Window = 80 };
        c.Expect("K8", [0x68], block: true, now: 1000);
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 1200, out _));
    }

    [Fact]
    public void NonCandidateKeysAreNeverPad()
    {
        var c = new SuppressionCorrelator();
        c.Expect("K8", [0x68], block: true, now: 0);
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x41, false, false, 30, out _));
    }

    [Fact]
    public void OnePhysicalPressSuppressesOneKeyDown()
    {
        var c = new SuppressionCorrelator();
        c.Expect("K8", [0x68], block: true, now: 0);
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x68, false, true, 1, out _));
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x68, true, true, 2, out _));
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 30, out _));
    }
}

public class SuppressionPassTests
{
    [Fact]
    public void PassthroughPressIsRecognizedWithoutBlocking()
    {
        var c = new SuppressionCorrelator();
        c.Expect("K8", [0x68], block: false, now: 0);
        Assert.Equal(SuppressionCorrelator.Verdict.PadPass, c.Classify(0x68, false, true, 2, out var ctl));
        Assert.Equal("K8", ctl);
        Assert.Equal(SuppressionCorrelator.Verdict.PadPass, c.Classify(0x68, true, true, 50, out _));
    }

    [Fact]
    public void PhysicalUpDropsUnmatchedExpectation()
    {
        var c = new SuppressionCorrelator();
        c.Expect("K8", [0x68], block: true, now: 0);
        c.PhysicalUp("K8");
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 30, out _));
    }
}

public class LeakDetectionTests
{
    [Fact]
    public void LatePhysicalReportCountsAsLeakAndDoesNotArm()
    {
        var c = new SuppressionCorrelator();
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 100, out _));
        c.NotePassed(0x68, 108);
        Assert.Equal(1, c.Expect("K8", [0x68], block: true, now: 115));
        // key up of the leaked press must pass too, and the next genuine press is not pre-armed
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x68, true, true, 150, out _));
        Assert.Equal(SuppressionCorrelator.Verdict.Unknown, c.Classify(0x68, false, true, 160, out _));
    }

    [Fact]
    public void OldPassesAreNotLeaks()
    {
        var c = new SuppressionCorrelator();
        c.NotePassed(0x68, 0);
        Assert.Equal(0, c.Expect("K8", [0x68], block: true, now: 500));
    }
}

public class WildcardTests
{
    [Fact]
    public void FirmwareModeSwitchedKeyIsStillCaught()
    {
        // knob press switched the firmware mode: K1 now types 'A' instead of Num1
        var c = new SuppressionCorrelator();
        c.Expect("K1", [0x61], block: true, now: 0);
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x41, false, false, 2, out var ctl));
        Assert.Equal("K1", ctl);
        Assert.Equal(SuppressionCorrelator.Verdict.PadBlock, c.Classify(0x41, true, false, 120, out _));
    }

    [Fact]
    public void WildcardExpiresQuickly()
    {
        var c = new SuppressionCorrelator { WildcardWindow = 25 };
        c.Expect("K1", [0x61], block: true, now: 0);
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x41, false, false, 26, out _));
    }

    [Fact]
    public void NoWildcardForControlsThatNeverType()
    {
        var c = new SuppressionCorrelator();
        c.Expect("KNOB_CW", [0xAF], block: true, now: 0, wildcard: false);
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x41, false, false, 1, out _));
    }

    [Fact]
    public void KeyPassedJustBeforePhysicalReportIsALeak()
    {
        var c = new SuppressionCorrelator();
        c.NotePassed(0x41, 100);
        Assert.Equal(1, c.Expect("K1", [0x61], block: true, now: 104));
        // and the wildcard was not armed, so the next main-keyboard key passes
        Assert.Equal(SuppressionCorrelator.Verdict.NotPad, c.Classify(0x42, false, false, 106, out _));
    }
}

public class LayerFallbackTests
{
    private static Router WithGameFallback(string fallback)
    {
        var c = HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));
        c.Layers.First(l => l.Id == "game").Fallback = fallback;
        Assert.Empty(c.Validate());
        var r = new Router(c);
        r.SetLayer(new LayerAction("set", "game"));
        return r;
    }

    [Fact]
    public void InheritUsesBaseLayer() =>
        Assert.Equal("pass", WithGameFallback("inherit").Route("K7", ControlPhase.Down, null).Function);

    [Fact]
    public void BlockSwallowsUnmappedControls()
    {
        var res = WithGameFallback("block").Route("K7", ControlPhase.Down, null);
        Assert.IsType<NoneAction>(res.Action);
        Assert.True(res.Block);
    }

    [Fact]
    public void PassthroughLetsUnmappedControlsThrough()
    {
        var r = WithGameFallback("passthrough");
        Assert.False(r.Route("K7", ControlPhase.Down, null).Block);
        Assert.Equal("game.forward", r.Route("JOY_UP", ControlPhase.Down, null).Function); // mapped controls unaffected
    }

    [Fact]
    public void ValidationChecksLayerReferences()
    {
        var c = HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));
        c.Layers[1].Fallback = "whatever";
        c.Apps[0].Layer = "gone";
        var errors = c.Validate();
        Assert.Contains(errors, e => e.Contains("fallback"));
        Assert.Contains(errors, e => e.Contains("unknown layer 'gone'"));
    }
}

public class ForwardModeTests
{
    private static HubConfig Defaults() => HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));

    [Fact]
    public void LegacyForwardAllMigratesToFunctions()
    {
        var json = """{ "layers": [ { "id": "a", "map": {} } ], "apps": [ { "id": "x", "processes": ["x.exe"], "forwardAll": true } ] }""";
        var c = System.Text.Json.JsonSerializer.Deserialize<HubConfig>(json, HubConfig.JsonOptions)!.Normalize();
        Assert.Equal(AppProfile.ForwardFunctions, c.Apps[0].Forward);
        Assert.Null(c.Apps[0].ForwardAll);
        Assert.DoesNotContain("forwardAll", System.Text.Json.JsonSerializer.Serialize(c, HubConfig.JsonOptions));
    }

    [Fact]
    public void FunctionsModeMarksRouteForward()
    {
        var c = Defaults();
        c.Apps.First(a => a.Id == "vscode").Forward = AppProfile.ForwardFunctions;
        var r = new Router(c);
        r.SetLayer(new LayerAction("set", "desktop"));
        Assert.True(r.Route("K1", ControlPhase.Down, "Code.exe").Forward);
        Assert.False(r.Route("K1", ControlPhase.Down, "notepad.exe").Forward);
    }

    [Fact]
    public void InvalidForwardValueIsRejected()
    {
        var c = Defaults();
        c.Apps[0].Forward = "everything";
        Assert.Contains(c.Validate(), e => e.Contains("forward must be"));
    }
}

public class NativeConsumerTests
{
    private static HubConfig Defaults() => HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));

    [Fact]
    public void KnobBoundToVolumeIsNotInjectedTwice()
    {
        var r = new Router(Defaults());
        r.SetLayer(new LayerAction("set", "desktop")); // KNOB_CW → media.volUp (VolumeUp)
        var res = r.Route("KNOB_CW", ControlPhase.Down, null);
        Assert.Equal("media.volUp", res.Function);
        Assert.IsType<PassthroughAction>(res.Action); // the knob already raised the volume natively
        Assert.False(res.Block);
    }

    [Fact]
    public void KnobBoundToSomethingElseStillBlocks()
    {
        var r = new Router(Defaults());
        r.SetLayer(new LayerAction("set", "unreal")); // KNOB_CW → edit.redo
        var res = r.Route("KNOB_CW", ControlPhase.Down, null);
        Assert.Equal(new KeysAction("Ctrl+Y"), res.Action);
        Assert.True(res.Block);
        Assert.Contains(0xE9, r.ConsumerUsagesForControl("KNOB_CW"));
    }

    [Theory]
    [InlineData("VolumeUp", false, true)]
    [InlineData("VolumeDown", false, false)] // other direction is a real change
    [InlineData("Ctrl+VolumeUp", false, false)]
    [InlineData("VolumeUp", true, false)] // hold actions are not plain taps
    public void DuplicateDetection(string keys, bool hold, bool expected) =>
        Assert.Equal(expected, NativeConsumer.DuplicatesNativeEffect(new KeysAction(keys, hold), [0xE9]));
}

public class PadTransportTests
{
    [Theory]
    [InlineData(@"\?\hid#vid_b6a4&pid_4100&mi_02&col03#7&1&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", PadTransport.Usb)]
    [InlineData(@"\?\hid#vid_b6a4&pid_4101&mi_02&col03#7&37e54c2c&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", PadTransport.Receiver)]
    [InlineData(@"\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002b6a4_pid&4102&col03#9&1&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", PadTransport.Bluetooth)]
    [InlineData(@"\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02b6a4_pid&4102_rev&0001&col03#a&1&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", PadTransport.Bluetooth)]
    public void RecognisesEachMode(string path, string expected) => Assert.Equal(expected, PadTransport.Of(path));

    [Fact]
    public void CableWinsWhenBothAreOpen() =>
        Assert.Equal(PadTransport.Usb, PadTransport.Of([@"\?\hid#vid_b6a4&pid_4101&mi_02", @"\?\hid#vid_b6a4&pid_4100&mi_02"]));
}

public class PadBatteryTests
{
    [Fact]
    public void ParsesRecordedReplies()
    {
        // Status replies recorded from the pad: 2.4G at 83 %, cable while charging (full).
        Assert.Equal(new PadBattery(83, false, true), PadBattery.Parse([0x01, 0x53, 0x00]));
        Assert.Equal(new PadBattery(100, true, false), PadBattery.Parse([0x00, 0xE4, 0x00]));
        Assert.Null(PadBattery.Parse([0x01, 0x7F, 0x00])); // 127 % is not a reading
    }
}
