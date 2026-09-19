using MacroHub.Core;
using Xunit;

namespace MacroHub.Core.Tests;

public class ControlStepperTests
{
    [Fact]
    public void DefaultTriggersEveryDetent()
    {
        var st = new ControlStepper();
        for (int i = 0; i < 5; i++) Assert.True(st.OnDown("KNOB_CW", "knob", InputSettings.Default, i * 30).Trigger);
    }

    [Fact]
    public void TriggersOncePerNDetents()
    {
        var st = new ControlStepper();
        var s = new InputSettings { StepDetents = 3 };
        var fired = Enumerable.Range(0, 7).Select(i => st.OnDown("KNOB_CW", "knob", s, i * 30).Trigger).ToArray();
        Assert.Equal([false, false, true, false, false, true, false], fired);
    }

    [Fact]
    public void ProgressIsReported()
    {
        var st = new ControlStepper();
        var s = new InputSettings { StepDetents = 3 };
        Assert.Equal(new ControlStepper.Result(false, 1, 3), st.OnDown("KNOB_CW", "knob", s, 0));
        Assert.Equal(new ControlStepper.Result(false, 2, 3), st.OnDown("KNOB_CW", "knob", s, 30));
        Assert.Equal(new ControlStepper.Result(true, 3, 3), st.OnDown("KNOB_CW", "knob", s, 60));
    }

    [Fact]
    public void IdleResetsPartialProgress()
    {
        var st = new ControlStepper();
        var s = new InputSettings { StepDetents = 2, ResetMs = 500 };
        Assert.False(st.OnDown("KNOB_CW", "knob", s, 0).Trigger);
        Assert.False(st.OnDown("KNOB_CW", "knob", s, 1000).Trigger); // idle → starts over
        Assert.True(st.OnDown("KNOB_CW", "knob", s, 1030).Trigger);
    }

    [Fact]
    public void TurningBackResetsProgress()
    {
        var st = new ControlStepper();
        var s = new InputSettings { StepDetents = 3 };
        st.OnDown("KNOB_CW", "knob", s, 0);
        st.OnDown("KNOB_CW", "knob", s, 30);
        Assert.False(st.OnDown("KNOB_CCW", "knob", s, 60).Trigger); // other direction starts at 1
        Assert.False(st.OnDown("KNOB_CW", "knob", s, 90).Trigger);  // CW progress was reset too
        Assert.False(st.OnDown("KNOB_CW", "knob", s, 120).Trigger);
        Assert.True(st.OnDown("KNOB_CW", "knob", s, 150).Trigger);
    }

    [Fact]
    public void MinIntervalThrottles()
    {
        var st = new ControlStepper();
        var s = new InputSettings { MinIntervalMs = 100 };
        var fired = new[] { 0, 30, 60, 110, 140, 230 }.Select(t => st.OnDown("KNOB_CW", "knob", s, t).Trigger).ToArray();
        Assert.Equal([true, false, false, true, false, true], fired);
    }
}

public class InputSettingsConfigTests
{
    private static HubConfig Defaults() => HubConfig.Load(Path.Combine(AppContext.BaseDirectory, "hub.json"));

    [Fact]
    public void LayerOverrideWinsOverControlSetting()
    {
        var c = Defaults();
        c.Device.Controls.First(x => x.Id == "KNOB_CW").Input = new InputSettings { StepDetents = 2 };
        c.Layers.First(l => l.Id == "desktop").Input = new() { ["KNOB_CW"] = new InputSettings { StepDetents = 4 } };
        Assert.Empty(c.Validate());
        var r = new Router(c);
        Assert.Equal(4, r.ResolveInput("KNOB_CW", "desktop").StepDetents);
        Assert.Equal(2, r.ResolveInput("KNOB_CW", "unreal").StepDetents);
        Assert.Same(InputSettings.Default, r.ResolveInput("K1", "unreal"));
    }

    [Fact]
    public void KnobPartsShareAGroupKeysDoNot()
    {
        var r = new Router(Defaults());
        Assert.NotNull(r.GroupOf("KNOB_CW"));
        Assert.Equal(r.GroupOf("KNOB_CW"), r.GroupOf("KNOB_CCW"));
        Assert.Null(r.GroupOf("K1"));
    }

    [Fact]
    public void RoundTripsAndValidatesRanges()
    {
        var c = Defaults();
        c.Device.Controls.First(x => x.Id == "JOY_UP").Input = new InputSettings { Repeat = true, RepeatDelayMs = 300, RepeatIntervalMs = 80 };
        var path = Path.GetTempFileName();
        c.Save(path);
        var again = HubConfig.Load(path);
        File.Delete(path);
        Assert.Equal(80, again.Device.Controls.First(x => x.Id == "JOY_UP").Input!.RepeatIntervalMs);

        again.Device.Controls.First(x => x.Id == "KNOB_CW").Input = new InputSettings { StepDetents = 0 };
        again.Layers[0].Input = new() { ["NOPE"] = new InputSettings { RepeatIntervalMs = 1 } };
        var errors = again.Validate();
        Assert.Contains(errors, e => e.Contains("stepDetents"));
        Assert.Contains(errors, e => e.Contains("unknown control 'NOPE'"));
        Assert.Contains(errors, e => e.Contains("repeatIntervalMs"));
    }
}
