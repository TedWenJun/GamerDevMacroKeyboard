// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "MacroKeyboardSettings.generated.h"

/** Project Settings → Plugins → MacroKeyboard. */
UCLASS(config = MacroKeyboard, defaultconfig, meta = (DisplayName = "MacroKeyboard"))
class MACROKEYBOARDRUNTIME_API UMacroKeyboardSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UMacroKeyboardSettings();

	/** Connect to MacroHub. Turn off to leave the pad to MacroHub's own shortcut layers. */
	UPROPERTY(EditAnywhere, config, Category = "Connection")
	bool bEnabled = true;

	/** Named pipe published by MacroHub (its --pipe argument), without the \\.\pipe\ prefix. */
	UPROPERTY(EditAnywhere, config, Category = "Connection")
	FString PipeName = TEXT("MacroHub");

	/** Identifies this client in MacroHub's web UI. */
	UPROPERTY(EditAnywhere, config, Category = "Connection")
	FString AppId = TEXT("unreal-editor");

	/**
	 * Write every raw control event received from MacroHub to the Output Log ("KNOB_CW down (knob/cw seq=…)").
	 * Off by default. What the editor then does with each event has its own switch in Editor Preferences →
	 * Plugins → MacroKeyboard (Editor) → Diagnostics.
	 */
	UPROPERTY(EditAnywhere, config, Category = "Diagnostics", meta = (DisplayName = "Log Raw Hub Events"))
	bool bLogEvents = false;

	/** Seconds between reconnect attempts while MacroHub is not running (grows up to 4x). */
	UPROPERTY(EditAnywhere, config, Category = "Connection", meta = (ClampMin = "0.5", ClampMax = "30.0"))
	float ReconnectSeconds = 2.0f;

	virtual FName GetCategoryName() const override { return TEXT("Plugins"); }

	static const UMacroKeyboardSettings& Get();
};
