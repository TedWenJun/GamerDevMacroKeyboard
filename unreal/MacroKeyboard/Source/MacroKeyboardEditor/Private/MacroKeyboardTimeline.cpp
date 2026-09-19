// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardTimeline.h"

#include "AnimPreviewInstance.h"
#include "Animation/DebugSkelMeshComponent.h"
#include "Editor.h"
#include "ISequencer.h"
#include "MacroKeyboardTypes.h"
#include "Engine/World.h"
#include "UObject/UObjectIterator.h"

namespace MacroKeyboardTimeline
{
	UAnimPreviewInstance* FindAnimationPreview(const FString& TabLabel)
	{
		// Unreal builds without RTTI, so the asset editor toolkit cannot be cast to IHasPersonaToolkit here.
		// The preview mesh of every open animation editor lives in an editor-preview world, which is enough to find it:
		// the active tab is labelled with the asset name, so prefer the preview playing that asset.
		UAnimPreviewInstance* Fallback = nullptr;
		for (TObjectIterator<UDebugSkelMeshComponent> It; It; ++It)
		{
			UDebugSkelMeshComponent* Mesh = *It;
			if (Mesh == nullptr || !IsValid(Mesh) || Mesh->IsTemplate())
			{
				continue;
			}
			const UWorld* World = Mesh->GetWorld();
			if (World == nullptr || World->WorldType != EWorldType::EditorPreview)
			{
				continue;
			}
			UAnimPreviewInstance* Preview = Mesh->PreviewInstance;
			if (Preview == nullptr)
			{
				continue;
			}
			const UAnimationAsset* Asset = Preview->GetAnimationAsset();
			if (Asset != nullptr && Asset->GetName() == TabLabel)
			{
				return Preview;
			}
			if (Fallback == nullptr && Asset != nullptr)
			{
				Fallback = Preview;
			}
		}
		return Fallback;
	}

	bool ScrubSequencer(const TSharedPtr<ISequencer>& Sequencer, int32 Frames)
	{
		if (!Sequencer.IsValid() || Frames == 0)
		{
			return false;
		}
		const FQualifiedFrameTime Current = Sequencer->GetLocalTime();
		const FFrameRate DisplayRate = Sequencer->GetFocusedDisplayRate();
		// One display frame expressed in the sequence's tick resolution.
		const FFrameTime Step = FFrameRate::TransformTime(FFrameTime(Frames), DisplayRate, Current.Rate);
		Sequencer->SetLocalTime(Current.Time + Step, ESnapTimeMode::STM_None);
		return true;
	}

	bool ScrubAnimation(UAnimPreviewInstance* Preview, int32 Frames)
	{
		if (Preview == nullptr || Frames == 0)
		{
			return false;
		}
		UAnimationAsset* Asset = Preview->GetAnimationAsset();
		if (Asset == nullptr)
		{
			return false;
		}
		// Step by frames of the animation's own sample rate, fall back to 30 fps for assets without one.
		double FrameSeconds = 1.0 / 30.0;
		if (const UAnimSequenceBase* Sequence = Cast<UAnimSequenceBase>(Asset))
		{
			const float Length = Sequence->GetPlayLength();
			const int32 NumFrames = Sequence->GetNumberOfSampledKeys();
			if (Length > 0.0f && NumFrames > 1)
			{
				FrameSeconds = Length / static_cast<double>(NumFrames - 1);
			}
		}

		Preview->SetPlaying(false); // scrubbing while playing would be overwritten by playback
		const float Length = Preview->GetLength();
		const float NewTime = FMath::Clamp(Preview->GetCurrentTime() + static_cast<float>(Frames * FrameSeconds), 0.0f, Length);
		Preview->SetPosition(NewTime);
		return true;
	}

	bool TogglePlayAnimation(UAnimPreviewInstance* Preview)
	{
		if (Preview == nullptr)
		{
			return false;
		}
		Preview->SetPlaying(!Preview->IsPlaying());
		return true;
	}

	int32 AccelerationFor(const FString& Control, double TimestampMs, bool bEnabled)
	{
		static FString LastControl;
		static double LastTimestampMs = 0.0;

		const double Delta = (Control == LastControl && LastTimestampMs > 0.0) ? TimestampMs - LastTimestampMs : TNumericLimits<double>::Max();
		LastControl = Control;
		LastTimestampMs = TimestampMs;

		if (!bEnabled)
		{
			return 1;
		}
		if (Delta < 40.0)
		{
			return 5;
		}
		if (Delta < 90.0)
		{
			return 2;
		}
		return 1;
	}
}
