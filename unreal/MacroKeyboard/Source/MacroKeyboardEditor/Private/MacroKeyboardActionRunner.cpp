// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardActionRunner.h"

#include "Editor.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Commands/InputBindingManager.h"
#include "Framework/Commands/InputChord.h"
#include "Framework/Commands/UICommandInfo.h"
#include "ISequencer.h"
#include "MacroKeyboardEditorSettings.h"
#include "MacroKeyboardTimeline.h"
#include "MacroKeyboardTypes.h"

bool FMacroKeyboardActionRunner::Execute(const FMacroKeyboardBinding& Binding, const FMacroControlEvent& Event, const FMacroKeyboardContext& Context)
{
	switch (Binding.Action)
	{
	case EMacroEditorAction::None:
		return true;

	case EMacroEditorAction::Chord:
		return SendChord(Binding.Chord);

	case EMacroEditorAction::UICommand:
		return RunUICommand(Binding.CommandContext, Binding.CommandName);

	case EMacroEditorAction::ConsoleCommand:
		return RunConsoleCommand(Binding.ConsoleCommand);

	case EMacroEditorAction::TimelineScrub:
		return RunTimelineScrub(Binding, Event, Context);

	case EMacroEditorAction::TimelinePlayPause:
		return RunTimelinePlayPause(Context);

	default:
		return false;
	}
}

bool FMacroKeyboardActionRunner::RunTimelineScrub(const FMacroKeyboardBinding& Binding, const FMacroControlEvent& Event, const FMacroKeyboardContext& Context)
{
	// Rotary controls scrub by their direction; keys and joystick directions use the sign of Amount.
	const int32 Direction = Event.IsRotary() ? Event.RotaryDirection() : 1;
	const int32 Multiplier = Event.IsRotary()
		? MacroKeyboardTimeline::AccelerationFor(Event.Control, Event.TimestampMs, Context.bKnobAcceleration) * FMath::Max(1, Context.HoldMultiplier)
		: 1;
	const int32 Frames = FMath::RoundToInt(Binding.Amount) * Direction * Multiplier;
	if (Frames == 0)
	{
		return false;
	}

	if (Context.Sequencer.IsValid())
	{
		return MacroKeyboardTimeline::ScrubSequencer(Context.Sequencer, Frames);
	}
	if (UAnimPreviewInstance* Preview = MacroKeyboardTimeline::FindAnimationPreview(Context.Detail))
	{
		return MacroKeyboardTimeline::ScrubAnimation(Preview, Frames);
	}
	UE_LOG(LogMacroKeyboard, Verbose, TEXT("no timeline in context '%s'"), *Context.Name);
	return false;
}

bool FMacroKeyboardActionRunner::RunTimelinePlayPause(const FMacroKeyboardContext& Context)
{
	if (Context.Sequencer.IsValid())
	{
		// Sequencer owns play state through its own command, so reuse it (respects the user's shortcut).
		return RunUICommand(TEXT("Sequencer"), TEXT("TogglePlay"));
	}
	if (UAnimPreviewInstance* Preview = MacroKeyboardTimeline::FindAnimationPreview(Context.Detail))
	{
		return MacroKeyboardTimeline::TogglePlayAnimation(Preview);
	}
	return false;
}

bool FMacroKeyboardActionRunner::SendChord(const FInputChord& Chord)
{
	if (!Chord.Key.IsValid())
	{
		UE_LOG(LogMacroKeyboard, Warning, TEXT("binding has no key set"));
		return false;
	}
	if (!FSlateApplication::IsInitialized())
	{
		return false;
	}

	const FModifierKeysState Modifiers(
		Chord.bShift != 0, false,
		Chord.bCtrl != 0, false,
		Chord.bAlt != 0, false,
		Chord.bCmd != 0, false,
		false);

	FSlateApplication& Slate = FSlateApplication::Get();
	const uint32* KeyCode = nullptr;
	const uint32* CharCode = nullptr;
	FInputKeyManager::Get().GetCodesFromKey(Chord.Key, KeyCode, CharCode);
	const FKeyEvent KeyEvent(Chord.Key, Modifiers, Slate.GetUserIndexForKeyboard(), /*bIsRepeat*/ false,
		CharCode != nullptr ? *CharCode : 0, KeyCode != nullptr ? *KeyCode : 0);

	const bool bHandled = Slate.ProcessKeyDownEvent(KeyEvent);
	Slate.ProcessKeyUpEvent(KeyEvent);
	if (!bHandled)
	{
		UE_LOG(LogMacroKeyboard, Verbose, TEXT("no widget handled %s"), *Chord.GetInputText().ToString());
	}
	return bHandled;
}

bool FMacroKeyboardActionRunner::RunUICommand(const FName& CommandContext, const FName& CommandName)
{
	const TSharedPtr<FUICommandInfo> Command = FInputBindingManager::Get().FindCommandInContext(CommandContext, CommandName);
	if (!Command.IsValid())
	{
		UE_LOG(LogMacroKeyboard, Warning, TEXT("command '%s.%s' not found — check the binding context and command name"),
			*CommandContext.ToString(), *CommandName.ToString());
		return false;
	}

	// Commands are executed by whichever command list the focused widget chain provides; reuse the user's own
	// shortcut for the command so the routing stays identical to pressing it on the keyboard.
	const TSharedRef<const FInputChord> Chord = Command->GetFirstValidChord();
	if (!Chord->IsValidChord())
	{
		UE_LOG(LogMacroKeyboard, Warning, TEXT("command '%s.%s' has no keyboard shortcut; assign one in Editor Preferences → Keyboard Shortcuts"),
			*CommandContext.ToString(), *CommandName.ToString());
		return false;
	}
	return SendChord(*Chord);
}

bool FMacroKeyboardActionRunner::RunConsoleCommand(const FString& Command)
{
	if (Command.IsEmpty())
	{
		return false;
	}
	if (GEditor == nullptr)
	{
		return false;
	}
	// PIE commands go to the running world, everything else to the editor world.
	UWorld* World = GEditor->PlayWorld != nullptr ? ToRawPtr(GEditor->PlayWorld) : GEditor->GetEditorWorldContext().World();
	if (World == nullptr)
	{
		return false;
	}
	return GEditor->Exec(World, *Command, *GLog);
}
