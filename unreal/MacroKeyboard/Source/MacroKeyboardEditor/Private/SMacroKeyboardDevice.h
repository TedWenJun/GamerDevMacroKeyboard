// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "MacroKeyboardTypes.h"
#include "Widgets/SLeafWidget.h"

DECLARE_DELEGATE_OneParam(FOnMacroControlPicked, FString);

namespace MacroKeyboardDevice
{
	/** Cap-sized version of a binding description, e.g. "chord Ctrl+Up" → "Ctrl+Up". */
	FString CompactSummary(const FString& Value);

	/** Name of a control in the editor language: knob and joystick parts by id, anything else as MacroHub labels it. */
	FText ControlName(const FString& Id, const FString& HubLabel);
}

/**
 * Draws the pad the way MacroHub's web UI draws it: a dark body with key wells, rounded key caps, a round knob
 * split into left/right/press zones and a round joystick split into four wedges plus a centre press.
 * Everything is painted directly (no child widgets), so knob and joystick zones can be real pie slices.
 */
class SMacroKeyboardDevice : public SLeafWidget
{
public:
	SLATE_BEGIN_ARGS(SMacroKeyboardDevice) {}
		/** Fired when the user clicks a key, a knob zone or a joystick wedge. */
		SLATE_EVENT(FOnMacroControlPicked, OnControlPicked)
		/** Control that is drawn as selected. */
		SLATE_ATTRIBUTE(FString, SelectedControl)
		/** Control that is drawn as physically pressed right now. */
		SLATE_ATTRIBUTE(FString, PressedControl)
	SLATE_END_ARGS()

	void Construct(const FArguments& InArgs);

	/** Device layout as reported by the Hub. */
	void SetControls(const TArray<FMacroControlInfo>& InControls);
	/** Short text drawn under a key's label (its binding in the edited context). */
	void SetSummaryProvider(TFunction<FString(const FString&)> InProvider) { SummaryProvider = MoveTemp(InProvider); }

	virtual FVector2D ComputeDesiredSize(float) const override;
	virtual int32 OnPaint(const FPaintArgs& Args, const FGeometry& AllottedGeometry, const FSlateRect& MyCullingRect,
		FSlateWindowElementList& OutDrawElements, int32 LayerId, const FWidgetStyle& InWidgetStyle, bool bParentEnabled) const override;
	virtual FReply OnMouseButtonDown(const FGeometry& MyGeometry, const FPointerEvent& MouseEvent) override;
	virtual FReply OnMouseMove(const FGeometry& MyGeometry, const FPointerEvent& MouseEvent) override;
	virtual void OnMouseLeave(const FPointerEvent& MouseEvent) override;
	virtual FCursorReply OnCursorQuery(const FGeometry& MyGeometry, const FPointerEvent& CursorEvent) const override;

private:
	enum class ERegionShape : uint8
	{
		Key,	// rectangular key cap
		Wedge,	// pie slice of a knob or joystick
		Disc	// centre press zone
	};

	struct FRegion
	{
		FString Id;
		FString Label;
		ERegionShape Shape = ERegionShape::Key;
		/** Key: top-left corner and size, both in local pixels. */
		FVector2f Pos = FVector2f::ZeroVector;
		FVector2f Size = FVector2f::ZeroVector;
		/** Wedge/disc: centre, radii and the angle range (radians, screen space, +Y down). */
		FVector2f Centre = FVector2f::ZeroVector;
		float RadiusInner = 0.0f;
		float RadiusOuter = 0.0f;
		float AngleStart = 0.0f;
		float AngleEnd = 0.0f;
		bool bAccent = false;
		bool bGray = false;
	};

	struct FGroup
	{
		bool bKnob = true;
		FVector2f Centre = FVector2f::ZeroVector;
		float Radius = 0.0f;
	};

	void RebuildLayout();
	const FRegion* HitTest(const FVector2f& Local) const;
	FText GetHoverTooltip() const;

	TArray<FMacroControlInfo> Controls;
	TArray<FRegion> Regions;
	TArray<FGroup> Groups;
	/** Recessed wells behind the key rows, in local pixels (x, y, width, height). */
	TArray<FVector4f> Wells;
	FVector2f BodySize = FVector2f(600.0f, 340.0f);

	FString HoveredControl;
	TAttribute<FString> SelectedControl;
	TAttribute<FString> PressedControl;
	FOnMacroControlPicked OnControlPicked;
	TFunction<FString(const FString&)> SummaryProvider;
};
