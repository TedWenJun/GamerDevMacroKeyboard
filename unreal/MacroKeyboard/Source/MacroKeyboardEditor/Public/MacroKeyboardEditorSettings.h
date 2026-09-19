// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "Framework/Commands/InputChord.h"
#include "MacroKeyboardTypes.h"
#include "MacroKeyboardEditorSettings.generated.h"

/** What a control does in a given editor context. */
UENUM()
enum class EMacroEditorAction : uint8
{
	/** Nothing (use to shadow a broader binding in one context). */
	None,
	/** Send a keyboard shortcut inside Unreal (Slate routing, not the operating system). */
	Chord,
	/** Look the shortcut up by command name and send it — survives users rebinding the command. */
	UICommand,
	/** Execute a console / editor command, e.g. "stat fps". */
	ConsoleCommand,
	/** Move the Sequencer playhead / animation preview time. Rotary controls scrub by their turn direction. */
	TimelineScrub,
	/** Play or pause the Sequencer / animation preview. */
	TimelinePlayPause
};

/** Backlight to apply while a given editor context is in front. */
USTRUCT()
struct FMacroContextLighting
{
	GENERATED_BODY()

	/** Context id; empty applies to every context that has none of its own. */
	UPROPERTY(EditAnywhere, Category = "Lighting")
	FString Context;

	UPROPERTY(EditAnywhere, Category = "Lighting")
	FMacroLightingSpec Lighting;
};

/**
 * One binding: "in this context, this control does this".
 * Context matching is exact; leave it empty to apply in every context that has no more specific binding.
 */
USTRUCT()
struct FMacroKeyboardBinding
{
	GENERATED_BODY()

	/** Context id reported by the plugin: levelEditor, sequencer, animation, animBlueprint, pie, simulate, editor… */
	UPROPERTY(EditAnywhere, Category = "Binding")
	FString Context;

	/** Control id from MacroHub: K1…K9, K0, KDOT, KENTER, KMINUS, KPLUS, KSPACE, KNOB_CW/CCW/PRESS, JOY_*. */
	UPROPERTY(EditAnywhere, Category = "Binding")
	FString Control;

	UPROPERTY(EditAnywhere, Category = "Binding")
	EMacroEditorAction Action = EMacroEditorAction::Chord;

	/** Shortcut to send (Action = Chord). */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (EditCondition = "Action == EMacroEditorAction::Chord"))
	FInputChord Chord;

	/** Binding context of the command, e.g. "LevelEditor", "Sequencer" (Action = UICommand). */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (EditCondition = "Action == EMacroEditorAction::UICommand"))
	FName CommandContext;

	/** Command name inside that context, e.g. "Undo" (Action = UICommand). */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (EditCondition = "Action == EMacroEditorAction::UICommand"))
	FName CommandName;

	/** Console / editor command (Action = ConsoleCommand). */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (EditCondition = "Action == EMacroEditorAction::ConsoleCommand"))
	FString ConsoleCommand;

	/** Frames per trigger; rotary controls use the direction of the turn (Action = TimelineScrub). */
	UPROPERTY(EditAnywhere, Category = "Binding")
	float Amount = 1.0f;

	/**
	 * Detents per trigger for a rotary control: the pad reports several steps per physical click, so 3 here means
	 * one click moves the timeline once. 1 acts on every step.
	 */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (ClampMin = "1", ClampMax = "20"))
	int32 DetentsPerTrigger = 1;

	/** Rotary controls: turning quickly moves further per detent (2x under 90 ms, 5x under 40 ms between detents). */
	UPROPERTY(EditAnywhere, Category = "Binding")
	bool bSpeedAcceleration = false;

	/**
	 * Rotary controls: how much further a detent moves while the knob is held down during the turn. 1 turns it off.
	 * When a context uses it, the knob press becomes a modifier there and its own binding runs as a click on release.
	 */
	UPROPERTY(EditAnywhere, Category = "Binding", meta = (ClampMin = "1", ClampMax = "100"))
	int32 HoldMultiplier = 1;

	/** Run on release instead of press. */
	UPROPERTY(EditAnywhere, Category = "Binding")
	bool bOnRelease = false;

	/** Name shown on the key cap in the MacroKeyboard panel; empty shows the action itself (e.g. the shortcut). */
	UPROPERTY(EditAnywhere, Category = "Binding")
	FString DisplayName;

	FString Describe() const;
};

/** Editor Preferences → Plugins → MacroKeyboard (Editor): which control does what in which editor. */
UCLASS(config = EditorPerProjectUserSettings, meta = (DisplayName = "MacroKeyboard (Editor)"))
class UMacroKeyboardEditorSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UMacroKeyboardEditorSettings();

	UPROPERTY(EditAnywhere, config, Category = "Bindings")
	TArray<FMacroKeyboardBinding> Bindings;

	/** Let the editor tint the pad: each context below gets its own backlight while it is in front. */
	UPROPERTY(EditAnywhere, config, Category = "Lighting")
	bool bDriveLighting = false;

	UPROPERTY(EditAnywhere, config, Category = "Lighting")
	TArray<FMacroContextLighting> ContextLighting;

	/**
	 * Write every control the editor acts on to the Output Log ("KNOB_CW [sequencer] -> timeline scrub…").
	 * Off by default; the same lines stay available at Verbose (`log LogMacroKeyboard Verbose`).
	 */
	UPROPERTY(EditAnywhere, config, Category = "Diagnostics", meta = (DisplayName = "Log Control Events"))
	bool bLogControlEvents = false;

	virtual FName GetCategoryName() const override { return TEXT("Plugins"); }

	/** Best match for a control: a binding for this context wins over a context-less one. */
	const FMacroKeyboardBinding* FindBinding(const FString& Context, const FString& Control) const;

	/** Lighting for a context: an exact match wins over the context-less entry; null when neither exists. */
	const FMacroContextLighting* FindLighting(const FString& Context) const;

	/** Bindings shipped with the plugin, used when the user has none yet. */
	static TArray<FMacroKeyboardBinding> DefaultBindings();

	static const UMacroKeyboardEditorSettings& Get();
};
