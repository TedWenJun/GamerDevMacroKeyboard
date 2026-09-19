// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "MacroKeyboardTypes.h"
#include "Widgets/SCompoundWidget.h"

class SMacroKeyboardDevice;
class SVerticalBox;
struct FMacroKeyboardBinding;

/**
 * Visual binding editor: draws the pad the way MacroHub's web UI does (layout comes from the Hub), lets the user
 * pick a control and edit what it does in the chosen editor context, and highlights controls as they are pressed.
 */
class SMacroKeyboardPanel : public SCompoundWidget
{
public:
	static const FName TabId;

	SLATE_BEGIN_ARGS(SMacroKeyboardPanel)
		: _ShowOpenInWindow(false)
		{}
		/** Embedded in the settings page: also offer a button that opens the panel as its own window. */
		SLATE_ARGUMENT(bool, ShowOpenInWindow)
	SLATE_END_ARGS()

	void Construct(const FArguments& InArgs);
	virtual ~SMacroKeyboardPanel() override;

	/** Select a control in the open panel (or in the next one that opens): `MacroKeyboard.OpenPanel KNOB_CW`. */
	static void SelectControl(const FString& ControlId);

private:
	/** Context currently being edited; empty string means "all contexts". */
	FString EditedContext;
	FString SelectedControl;
	/** Live highlight of the control the user just pressed. */
	FString PressedControl;
	double PressedAtSeconds = 0.0;
	/** Follow the context the editor reports instead of a fixed choice. */
	bool bFollowLiveContext = true;

	TSharedPtr<SMacroKeyboardDevice> Device;
	/** Last context the editor reported that was not the panel's own tab. */
	FString LastLiveContext;
	TSharedPtr<SVerticalBox> DetailsBox;
	TArray<TSharedPtr<FString>> ContextOptions;
	FDelegateHandle EventHandle;
	FDelegateHandle DeviceHandle;

	void RebuildDevice();
	/** Lighting controls shown beside the device: mode, brightness, colour, speed, direction. */
	TSharedRef<SWidget> BuildLighting();
	/** Entry being edited: the one for this context when it has its own, otherwise the shared one. */
	struct FMacroContextLighting* FindLightingEntry(bool bContextOnly = false) const;
	FMacroLightingSpec EditedLighting() const;
	void SetLighting(TFunctionRef<void(FMacroLightingSpec&)> Edit);
	void PushLighting();
	void RebuildDetails();
	/** Footer of the binding card: restore the shipped bindings, behind a confirmation. */
	void AddRestoreDefaults();
	void RefreshContextOptions();

	FMacroKeyboardBinding* FindBinding(const FString& Context, const FString& Control) const;
	/** Existing binding for the edited context, creating one if the user starts editing. */
	FMacroKeyboardBinding& EnsureBinding();
	void RemoveBinding();
	void SaveSettings();

	FString BindingSummary(const FString& ControlId) const;
	FString ActiveContext() const;

	void HandleControlEvent(const FMacroControlEvent& Event);
	EActiveTimerReturnType TickHighlight(double InCurrentTime, float InDeltaTime);

	/** The panel is a single nomad tab, so one weak pointer is enough to reach it from a console command. */
	static TWeakPtr<SMacroKeyboardPanel> LivePanel;
	static FString PendingSelection;
};
