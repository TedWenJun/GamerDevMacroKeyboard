using System.Text.Json.Serialization;

namespace MacroHub.Core;

/// <summary>What happens when a function fires. Serialized with a "type" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeysAction), "keys")]
[JsonDerivedType(typeof(TextAction), "text")]
[JsonDerivedType(typeof(MacroAction), "macro")]
[JsonDerivedType(typeof(RunAction), "run")]
[JsonDerivedType(typeof(MouseAction), "mouse")]
[JsonDerivedType(typeof(LayerAction), "layer")]
[JsonDerivedType(typeof(ForwardAction), "forward")]
[JsonDerivedType(typeof(PassthroughAction), "passthrough")]
[JsonDerivedType(typeof(NoneAction), "none")]
public abstract record ActionSpec
{
    /// <summary>Human readable one-liner for logs and the web UI.</summary>
    public abstract string Describe();
}

/// <summary>
/// Key chord(s), e.g. "Ctrl+Shift+S" or a chord sequence "Ctrl+K, Ctrl+C".
/// With <see cref="Hold"/> the (single) chord is pressed on control Down and released on Up — needed for games.
/// </summary>
public sealed record KeysAction(string Keys, bool Hold = false) : ActionSpec
{
    public override string Describe() => Hold ? $"hold {Keys}" : Keys;
}

public sealed record TextAction(string Text) : ActionSpec
{
    public override string Describe() => $"text \"{Text}\"";
}

public sealed record MacroStep(string? Keys = null, string? Text = null, int DelayMs = 0);

public sealed record MacroAction(List<MacroStep> Steps) : ActionSpec
{
    public override string Describe() => $"macro ({Steps.Count} steps)";
}

public sealed record RunAction(string Path, string? Args = null) : ActionSpec
{
    public override string Describe() => $"run {Path} {Args}".TrimEnd();
}

/// <summary>Mouse button click/hold or wheel ticks. Button: Left, Right, Middle, X1, X2.</summary>
public sealed record MouseAction(string? Button = null, int Wheel = 0, int HWheel = 0, bool Hold = false) : ActionSpec
{
    public override string Describe() => Button is not null ? $"mouse {Button}" : $"wheel {Wheel},{HWheel}";
}

/// <summary>Layer control. Op: next, prev, set, momentary (active while held).</summary>
public sealed record LayerAction(string Op, string? Layer = null) : ActionSpec
{
    public override string Describe() => Layer is null ? $"layer {Op}" : $"layer {Op} {Layer}";
}

/// <summary>Only deliver the function event to a connected application client (e.g. the UE plugin).</summary>
public sealed record ForwardAction : ActionSpec
{
    public override string Describe() => "forward to app";
}

/// <summary>Let the pad's original key reach the system untouched.</summary>
public sealed record PassthroughAction : ActionSpec
{
    public override string Describe() => "passthrough";
}

public sealed record NoneAction : ActionSpec
{
    public override string Describe() => "none (swallow)";
}
