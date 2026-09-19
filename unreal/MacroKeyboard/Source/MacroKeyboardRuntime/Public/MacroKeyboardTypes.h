// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "MacroKeyboardTypes.generated.h"

MACROKEYBOARDRUNTIME_API DECLARE_LOG_CATEGORY_EXTERN(LogMacroKeyboard, Log, All);

/** Press or release of a control. Rotary controls send one Down/Up pair per detent. */
UENUM(BlueprintType)
enum class EMacroControlPhase : uint8
{
	Down,
	Up
};

/**
 * One control event as sent by MacroHub (protocol v2, "control" message).
 * Control ids come from the Hub's device map, e.g. K1, KNOB_CW, JOY_UP.
 */
USTRUCT(BlueprintType)
struct FMacroControlEvent
{
	GENERATED_BODY()

	/** Logical control id, e.g. "K1", "KNOB_CW", "JOY_PRESS". */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Control;

	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	EMacroControlPhase Phase = EMacroControlPhase::Down;

	/** "key", "knob" or "joystick". */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Kind;

	/** Sub-part of a knob/joystick: cw, ccw, press, up, down, left, right. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Part;

	/** "pad" for the physical device, "simulate" for the Hub's web UI / tests. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Source;

	/** Per-connection sequence number; a gap means the game thread stalled and the Hub dropped queued events. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	int64 Seq = 0;

	/** Hub monotonic timestamp in milliseconds — use the delta between detents for knob acceleration. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	double TimestampMs = 0.0;

	bool IsDown() const { return Phase == EMacroControlPhase::Down; }
	bool IsRotary() const { return Part == TEXT("cw") || Part == TEXT("ccw"); }
	/** +1 for clockwise, -1 for counter-clockwise, 0 for anything else. */
	int32 RotaryDirection() const { return Part == TEXT("cw") ? 1 : (Part == TEXT("ccw") ? -1 : 0); }

	FString ToString() const
	{
		return FString::Printf(TEXT("%s %s (%s%s%s seq=%lld)"), *Control, IsDown() ? TEXT("down") : TEXT("up"),
			*Kind, Part.IsEmpty() ? TEXT("") : TEXT("/"), *Part, Seq);
	}
};

/**
 * A control as described by MacroHub (welcome / profile message): what it is called and where it sits on the device,
 * so a UI can draw the pad without knowing the hardware.
 */
USTRUCT(BlueprintType)
struct FMacroControlInfo
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Id;

	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Label;

	/** "key", "knob" or "joystick". */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Kind;

	/** Sub-part of a knob/joystick: cw, ccw, press, up, down, left, right. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FString Part;

	/** Layout rectangle in key-width units: position and size on the device. */
	UPROPERTY(BlueprintReadOnly, Category = "MacroKeyboard")
	FVector4 Rect = FVector4(0, 0, 1, 1);
};

/** Connection state of the MacroHub client. */
UENUM(BlueprintType)
enum class EMacroHubConnection : uint8
{
	Disabled,
	Connecting,
	Connected
};

/**
 * Backlight state of the pad, mirroring what MacroHub's own lighting panel offers. The Hub clamps anything out of
 * range, so these are the values the firmware actually has.
 */
USTRUCT(BlueprintType)
struct FMacroLightingSpec
{
	GENERATED_BODY()

	/** Effect: 1 Solid · 2 Flowing · 3 Marquee · 4 Breathing · 5 Cycling breath · 6 Tetris · 7 Neon · 8 Rainbow flow · 9 Off */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "MacroKeyboard", meta = (ClampMin = "1", ClampMax = "9"))
	int32 Mode = 1;

	/** 1 (dimmest) to 6 (brightest). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "MacroKeyboard", meta = (ClampMin = "1", ClampMax = "6"))
	int32 Brightness = 5;

	/** 0 (slowest) to 5 (fastest); only the animated effects use it. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "MacroKeyboard", meta = (ClampMin = "0", ClampMax = "5"))
	int32 Speed = 3;

	/** 0 or 1; only the flowing effects use it. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "MacroKeyboard", meta = (ClampMin = "0", ClampMax = "1"))
	int32 Direction = 0;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "MacroKeyboard")
	FLinearColor Color = FLinearColor::White;

	/** #rrggbb as the Hub protocol wants it. */
	FString ColorHex() const
	{
		const FColor Srgb = Color.ToFColor(true);
		return FString::Printf(TEXT("#%02x%02x%02x"), Srgb.R, Srgb.G, Srgb.B);
	}

	bool operator==(const FMacroLightingSpec& Other) const
	{
		return Mode == Other.Mode && Brightness == Other.Brightness && Speed == Other.Speed
			&& Direction == Other.Direction && Color.Equals(Other.Color, 0.001f);
	}
	bool operator!=(const FMacroLightingSpec& Other) const { return !(*this == Other); }
};
