// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardEditorSettings.h"

#include "InputCoreTypes.h"

#define LOCTEXT_NAMESPACE "MacroKeyboard"

namespace
{
	FMacroKeyboardBinding MakeChord(const TCHAR* Context, const TCHAR* Control, const FKey& Key,
		bool bCtrl = false, bool bShift = false, bool bAlt = false)
	{
		FMacroKeyboardBinding Binding;
		Binding.Context = Context;
		Binding.Control = Control;
		Binding.Action = EMacroEditorAction::Chord;
		Binding.Chord = FInputChord(Key, bShift, bCtrl, bAlt, false);
		return Binding;
	}

	FMacroKeyboardBinding MakeScrub(const TCHAR* Context, const TCHAR* Control, float Frames)
	{
		FMacroKeyboardBinding Binding;
		Binding.Context = Context;
		Binding.Control = Control;
		Binding.Action = EMacroEditorAction::TimelineScrub;
		Binding.Amount = Frames;
		// The W909 firmware sends two volume presses 12-40 ms apart per physical click (measured with
		// tools/InputRecorder), so out of the box one click moves one frame.
		Binding.DetentsPerTrigger = 2;
		return Binding;
	}

	FMacroKeyboardBinding MakePlayPause(const TCHAR* Context, const TCHAR* Control)
	{
		FMacroKeyboardBinding Binding;
		Binding.Context = Context;
		Binding.Control = Control;
		Binding.Action = EMacroEditorAction::TimelinePlayPause;
		return Binding;
	}

	FMacroKeyboardBinding MakeCommand(const TCHAR* Context, const TCHAR* Control, const TCHAR* CommandContext, const TCHAR* CommandName)
	{
		FMacroKeyboardBinding Binding;
		Binding.Context = Context;
		Binding.Control = Control;
		Binding.Action = EMacroEditorAction::UICommand;
		Binding.CommandContext = CommandContext;
		Binding.CommandName = CommandName;
		return Binding;
	}

	FMacroKeyboardBinding MakeConsole(const TCHAR* Context, const TCHAR* Control, const TCHAR* Command)
	{
		FMacroKeyboardBinding Binding;
		Binding.Context = Context;
		Binding.Control = Control;
		Binding.Action = EMacroEditorAction::ConsoleCommand;
		Binding.ConsoleCommand = Command;
		return Binding;
	}
}

FString FMacroKeyboardBinding::Describe() const
{
	switch (Action)
	{
	case EMacroEditorAction::Chord:
		return FString::Printf(TEXT("chord %s"), *Chord.GetInputText().ToString());
	case EMacroEditorAction::UICommand:
		return FString::Printf(TEXT("command %s.%s"), *CommandContext.ToString(), *CommandName.ToString());
	case EMacroEditorAction::ConsoleCommand:
		return FString::Printf(TEXT("console '%s'"), *ConsoleCommand);
	case EMacroEditorAction::TimelineScrub:
		return DetentsPerTrigger > 1
			? FString::Printf(TEXT("timeline scrub %.0f frame(s) / %d detents"), Amount, DetentsPerTrigger)
			: FString::Printf(TEXT("timeline scrub %.0f frame(s)"), Amount);
	case EMacroEditorAction::TimelinePlayPause:
		return TEXT("timeline play/pause");
	default:
		return TEXT("none");
	}
}

UMacroKeyboardEditorSettings::UMacroKeyboardEditorSettings()
{
	CategoryName = TEXT("Plugins");
	SectionName = TEXT("MacroKeyboardEditor");
	if (Bindings.Num() == 0)
	{
		Bindings = DefaultBindings();
	}
}

const UMacroKeyboardEditorSettings& UMacroKeyboardEditorSettings::Get()
{
	const UMacroKeyboardEditorSettings* Settings = GetDefault<UMacroKeyboardEditorSettings>();
	check(Settings != nullptr);
	return *Settings;
}

TArray<FMacroKeyboardBinding> UMacroKeyboardEditorSettings::DefaultBindings()
{
	TArray<FMacroKeyboardBinding> Defaults;

	// Level editor: the shortcuts a level designer reaches for most often.
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K1"), EKeys::P, false, false, true));        // Alt+P  Play in editor
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K2"), EKeys::S, false, false, true));        // Alt+S  Simulate
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K3"), EKeys::Escape));                       // Esc    Stop
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K4"), EKeys::F11, true, false, true));       // Ctrl+Alt+F11 Live Coding
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K5"), EKeys::S, true, true));                // Ctrl+Shift+S Save all
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K7"), EKeys::W));                            // move
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K8"), EKeys::E));                            // rotate
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K9"), EKeys::R));                            // scale
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("K0"), EKeys::F));                            // focus selection
	Defaults.Add(MakeChord(TEXT("levelEditor"), TEXT("KSPACE"), EKeys::SpaceBar, true));           // Ctrl+Space content drawer

	// The knob press is a pure programmable key on the W909 (it types nothing): open the binding panel with it.
	Defaults.Add(MakeConsole(TEXT(""), TEXT("KNOB_PRESS"), TEXT("MacroKeyboard.OpenPanel")));

	// Anywhere in the editor: undo/redo on the knob, navigation on the joystick.
	Defaults.Add(MakeChord(TEXT(""), TEXT("KNOB_CCW"), EKeys::Z, true));                           // Ctrl+Z
	Defaults.Add(MakeChord(TEXT(""), TEXT("KNOB_CW"), EKeys::Y, true));                            // Ctrl+Y
	Defaults.Add(MakeChord(TEXT(""), TEXT("JOY_UP"), EKeys::Up));
	Defaults.Add(MakeChord(TEXT(""), TEXT("JOY_DOWN"), EKeys::Down));
	Defaults.Add(MakeChord(TEXT(""), TEXT("JOY_LEFT"), EKeys::Left));
	Defaults.Add(MakeChord(TEXT(""), TEXT("JOY_RIGHT"), EKeys::Right));
	Defaults.Add(MakeChord(TEXT(""), TEXT("JOY_PRESS"), EKeys::Enter));
	Defaults.Add(MakeChord(TEXT(""), TEXT("KENTER"), EKeys::Enter));

	// Sequencer: knob scrubs the timeline, joystick jumps keys, space toggles playback through its own command.
	Defaults.Add(MakeScrub(TEXT("sequencer"), TEXT("KNOB_CW"), 1.0f));
	Defaults.Add(MakeScrub(TEXT("sequencer"), TEXT("KNOB_CCW"), 1.0f));
	Defaults.Add(MakePlayPause(TEXT("sequencer"), TEXT("KSPACE")));
	Defaults.Add(MakeCommand(TEXT("sequencer"), TEXT("JOY_LEFT"), TEXT("Sequencer"), TEXT("StepToPreviousKey")));
	Defaults.Add(MakeCommand(TEXT("sequencer"), TEXT("JOY_RIGHT"), TEXT("Sequencer"), TEXT("StepToNextKey")));
	Defaults.Add(MakeCommand(TEXT("sequencer"), TEXT("JOY_PRESS"), TEXT("Sequencer"), TEXT("SetSelectionRangeToPlayhead")));

	// Animation editor: same feel on the preview timeline.
	Defaults.Add(MakeScrub(TEXT("animation"), TEXT("KNOB_CW"), 1.0f));
	Defaults.Add(MakeScrub(TEXT("animation"), TEXT("KNOB_CCW"), 1.0f));
	Defaults.Add(MakePlayPause(TEXT("animation"), TEXT("KSPACE")));

	// PIE: debug helpers that do not need Enhanced Input (milestone 4 adds injected input actions).
	Defaults.Add(MakeConsole(TEXT("pie"), TEXT("K1"), TEXT("stat fps")));
	Defaults.Add(MakeConsole(TEXT("pie"), TEXT("K2"), TEXT("stat unit")));
	Defaults.Add(MakeConsole(TEXT("pie"), TEXT("K3"), TEXT("show collision")));

	return Defaults;
}

const FMacroContextLighting* UMacroKeyboardEditorSettings::FindLighting(const FString& Context) const
{
	const FMacroContextLighting* Fallback = nullptr;
	for (const FMacroContextLighting& Entry : ContextLighting)
	{
		if (Entry.Context == Context)
		{
			return &Entry; // exact context wins
		}
		if (Entry.Context.IsEmpty() && Fallback == nullptr)
		{
			Fallback = &Entry;
		}
	}
	return Fallback;
}

const FMacroKeyboardBinding* UMacroKeyboardEditorSettings::FindBinding(const FString& Context, const FString& Control) const
{
	const FMacroKeyboardBinding* Fallback = nullptr;
	for (const FMacroKeyboardBinding& Binding : Bindings)
	{
		if (Binding.Control != Control)
		{
			continue;
		}
		if (Binding.Context == Context)
		{
			return &Binding; // exact context wins
		}
		if (Binding.Context.IsEmpty() && Fallback == nullptr)
		{
			Fallback = &Binding;
		}
	}
	return Fallback;
}

#undef LOCTEXT_NAMESPACE
