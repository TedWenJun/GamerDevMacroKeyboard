// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"

class ISequencer;
class UAnimPreviewInstance;

/**
 * Timeline control for the editors that have one: Sequencer and the animation editor (Persona preview).
 * Everything here runs on the game thread.
 */
namespace MacroKeyboardTimeline
{
	/** Preview animation instance of the animation editor the user is looking at, matched by the active tab label. */
	UAnimPreviewInstance* FindAnimationPreview(const FString& TabLabel);

	/** Moves the Sequencer playhead by whole display-rate frames. */
	bool ScrubSequencer(const TSharedPtr<ISequencer>& Sequencer, int32 Frames);

	/** Moves the animation preview by frames of its own sample rate; stops playback so scrubbing takes effect. */
	bool ScrubAnimation(UAnimPreviewInstance* Preview, int32 Frames);

	bool TogglePlayAnimation(UAnimPreviewInstance* Preview);

	/**
	 * Detent-rate based multiplier: turning the knob quickly moves further per detent, which is what makes a rotary
	 * control usable as a scrub wheel. Uses the Hub timestamps (ms) of successive detents of the same control.
	 */
	int32 AccelerationFor(const FString& Control, double TimestampMs, bool bEnabled);
}
