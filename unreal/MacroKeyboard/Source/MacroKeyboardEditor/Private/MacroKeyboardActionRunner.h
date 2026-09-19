// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"

class ISequencer;
struct FInputChord;
struct FMacroControlEvent;
struct FMacroKeyboardBinding;

/** What the editor module knows about the current situation when a control arrives. */
struct FMacroKeyboardContext
{
	/** Context id, e.g. levelEditor / sequencer / animation / pie. */
	FString Name;
	/** Active tab label — used to pick the right animation editor when several are open. */
	FString Detail;
	/** Sequencer the user is working in, if any. */
	TSharedPtr<ISequencer> Sequencer;
	/** Speed up scrubbing when the knob is turned quickly. */
	bool bKnobAcceleration = true;
	/** Extra factor while the knob is held down during the turn (1 = none). */
	int32 HoldMultiplier = 1;
};

/** Executes a binding inside the editor. All calls happen on the game thread. */
class FMacroKeyboardActionRunner
{
public:
	/** Returns false when the binding could not be carried out (reason is logged). */
	static bool Execute(const FMacroKeyboardBinding& Binding, const FMacroControlEvent& Event, const FMacroKeyboardContext& Context);

private:
	/**
	 * Sends a shortcut through Slate rather than the operating system: the event goes to the focused widget chain,
	 * so it reaches exactly the editor the user is looking at and respects their own key bindings.
	 */
	static bool SendChord(const FInputChord& Chord);
	static bool RunConsoleCommand(const FString& Command);
	static bool RunUICommand(const FName& CommandContext, const FName& CommandName);
	static bool RunTimelineScrub(const FMacroKeyboardBinding& Binding, const FMacroControlEvent& Event, const FMacroKeyboardContext& Context);
	static bool RunTimelinePlayPause(const FMacroKeyboardContext& Context);
};
