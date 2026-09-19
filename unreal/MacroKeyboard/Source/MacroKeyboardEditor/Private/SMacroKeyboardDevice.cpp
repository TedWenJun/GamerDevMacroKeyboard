// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "SMacroKeyboardDevice.h"

#include "Brushes/SlateRoundedBoxBrush.h"
#include "Fonts/FontMeasure.h"
#include "Framework/Application/SlateApplication.h"
#include "Rendering/DrawElements.h"
#include "Rendering/SlateRenderer.h"
#include "Styling/AppStyle.h"

#define LOCTEXT_NAMESPACE "MacroKeyboard"

namespace MacroKeyboardDevice
{
	/** Pixels per key unit of the layout the Hub sends (the web UI uses the same unit grid). */
	constexpr float U = 76.0f;
	/** The Hub's origin is the top-left control; the body extends a quarter unit beyond it on every side. */
	constexpr float Pad = 0.25f;

	FLinearColor Hex(const TCHAR* Value) { return FLinearColor(FColor::FromHex(Value)); }

	// Palette lifted from the web UI so both front-ends look like the same product.
	const FLinearColor ColBody = Hex(TEXT("#121315"));
	const FLinearColor ColBodyEdge = Hex(TEXT("#2b2e33"));
	const FLinearColor ColWell = Hex(TEXT("#0b0c0d"));
	const FLinearColor ColCap = Hex(TEXT("#2c2f35"));
	const FLinearColor ColCapEdge = Hex(TEXT("#08090a"));
	const FLinearColor ColTop = Hex(TEXT("#383c43"));
	const FLinearColor ColGray = Hex(TEXT("#5b6068"));
	const FLinearColor ColAccent = Hex(TEXT("#f5c400"));
	const FLinearColor ColInk = Hex(TEXT("#1a1a1a"));
	const FLinearColor ColText = Hex(TEXT("#e8e9eb"));
	const FLinearColor ColSummary = Hex(TEXT("#c9ccd1"));
	const FLinearColor ColUnbound = Hex(TEXT("#6b7079"));
	const FLinearColor ColAccentSummary = Hex(TEXT("#3a3000"));

	const TSet<FString> AccentKeys = { TEXT("K1"), TEXT("KENTER"), TEXT("KMINUS") };
	const TSet<FString> GrayKeys = { TEXT("K5"), TEXT("K6"), TEXT("K9"), TEXT("K0"), TEXT("KDOT") };

	const FSlateBrush& BodyBrush() { static const FSlateRoundedBoxBrush B(FLinearColor::White, 22.0f); return B; }
	const FSlateBrush& WellBrush() { static const FSlateRoundedBoxBrush B(FLinearColor::White, 9.0f); return B; }
	const FSlateBrush& CapBrush() { static const FSlateRoundedBoxBrush B(FLinearColor::White, 8.0f); return B; }
	const FSlateBrush& TopBrush() { static const FSlateRoundedBoxBrush B(FLinearColor::White, 6.0f); return B; }

	FSlateFontInfo KeyFont(int32 Size, bool bBold)
	{
		FSlateFontInfo Font = FAppStyle::Get().GetFontStyle(bBold ? TEXT("NormalFontBold") : TEXT("NormalFont"));
		Font.Size = Size;
		return Font;
	}

	/** Cap-sized version of a binding description: the action word is implied by the editor below. */
	FString CompactSummary(const FString& Value)
	{
		FString Text = Value;
		FString Prefix;
		if (Text.StartsWith(TEXT("↳ ")))
		{
			Prefix = TEXT("↳ ");
			Text = Text.RightChop(2);
		}

		if (Text.StartsWith(TEXT("chord ")))
		{
			Text = Text.RightChop(6);
		}
		else if (Text.StartsWith(TEXT("command ")))
		{
			Text = Text.RightChop(8);
			int32 Dot = INDEX_NONE;
			if (Text.FindLastChar(TEXT('.'), Dot))
			{
				Text = Text.RightChop(Dot + 1);
			}
		}
		else if (Text.StartsWith(TEXT("console '")))
		{
			Text = Text.Mid(9, Text.Len() - 10);
		}
		else if (Text.StartsWith(TEXT("timeline scrub ")))
		{
			Text = Text.Replace(TEXT("timeline scrub "), TEXT(""))
				.Replace(TEXT(" frame(s)"), *LOCTEXT("CapFrames", " fr").ToString())
				.Replace(TEXT(" detents"), *LOCTEXT("CapDetents", " det").ToString());
		}
		else if (Text == TEXT("timeline play/pause"))
		{
			Text = LOCTEXT("CapPlayPause", "Play/Pause").ToString();
		}
		else if (Text == TEXT("none"))
		{
			Text = LOCTEXT("CapBlocked", "Blocked").ToString();
		}
		return Prefix + Text;
	}

	FText ControlName(const FString& Id, const FString& HubLabel)
	{
		static const TMap<FString, FText> Names = {
			{ TEXT("KNOB_CCW"), LOCTEXT("CtrlKnobCcw", "Knob left") },
			{ TEXT("KNOB_CW"), LOCTEXT("CtrlKnobCw", "Knob right") },
			{ TEXT("KNOB_PRESS"), LOCTEXT("CtrlKnobPress", "Knob press") },
			{ TEXT("JOY_UP"), LOCTEXT("CtrlJoyUp", "Joystick up") },
			{ TEXT("JOY_DOWN"), LOCTEXT("CtrlJoyDown", "Joystick down") },
			{ TEXT("JOY_LEFT"), LOCTEXT("CtrlJoyLeft", "Joystick left") },
			{ TEXT("JOY_RIGHT"), LOCTEXT("CtrlJoyRight", "Joystick right") },
			{ TEXT("JOY_PRESS"), LOCTEXT("CtrlJoyPress", "Joystick press") },
		};
		if (const FText* Name = Names.Find(Id))
		{
			return *Name;
		}
		return FText::FromString(HubLabel.IsEmpty() ? Id : HubLabel);
	}

	/** Trim to what actually fits on the key cap. */
	FString FitToWidth(const FString& Value, const FSlateFontInfo& Font, float MaxWidth, const TSharedRef<class FSlateFontMeasure>& Measure)
	{
		if (Value.IsEmpty() || static_cast<float>(Measure->Measure(Value, Font).X) <= MaxWidth)
		{
			return Value;
		}
		FString Text = Value;
		while (Text.Len() > 1 && static_cast<float>(Measure->Measure(Text + TEXT("…"), Font).X) > MaxWidth)
		{
			Text.LeftChopInline(1);
		}
		return Text + TEXT("…");
	}
}

using namespace MacroKeyboardDevice;

void SMacroKeyboardDevice::Construct(const FArguments& InArgs)
{
	OnControlPicked = InArgs._OnControlPicked;
	SelectedControl = InArgs._SelectedControl;
	PressedControl = InArgs._PressedControl;
	SetToolTipText(TAttribute<FText>::CreateSP(this, &SMacroKeyboardDevice::GetHoverTooltip));
	RebuildLayout();
}

void SMacroKeyboardDevice::SetControls(const TArray<FMacroControlInfo>& InControls)
{
	Controls = InControls;
	RebuildLayout();
	Invalidate(EInvalidateWidgetReason::Layout);
}

FVector2D SMacroKeyboardDevice::ComputeDesiredSize(float) const
{
	return FVector2D(BodySize);
}

void SMacroKeyboardDevice::RebuildLayout()
{
	Regions.Reset();
	Groups.Reset();
	Wells.Reset();

	// Knob and joystick parts share one rect, so each group is laid out once from its first part.
	TMap<FString, TArray<const FMacroControlInfo*>> GroupParts;
	TArray<FString> GroupOrder;
	float MaxX = 0.0f;
	float MaxY = 0.0f;

	for (const FMacroControlInfo& Info : Controls)
	{
		const float X = (static_cast<float>(Info.Rect.X) + Pad) * U;
		const float Y = (static_cast<float>(Info.Rect.Y) + Pad) * U;
		const float W = static_cast<float>(Info.Rect.Z) * U;
		const float H = static_cast<float>(Info.Rect.W) * U;
		MaxX = FMath::Max(MaxX, X + W);
		MaxY = FMath::Max(MaxY, Y + H);

		if (Info.Part.IsEmpty())
		{
			FRegion& Region = Regions.AddDefaulted_GetRef();
			Region.Id = Info.Id;
			Region.Label = Info.Label;
			Region.Shape = ERegionShape::Key;
			Region.Pos = FVector2f(X + 0.04f * U, Y + 0.04f * U);
			Region.Size = FVector2f(W - 0.08f * U, H - 0.08f * U);
			Region.bAccent = AccentKeys.Contains(Info.Id);
			Region.bGray = GrayKeys.Contains(Info.Id);
			continue;
		}

		const FString Key = Info.Kind + FString::Printf(TEXT("%.2f,%.2f"), Info.Rect.X, Info.Rect.Y);
		if (!GroupParts.Contains(Key))
		{
			GroupOrder.Add(Key);
		}
		GroupParts.FindOrAdd(Key).Add(&Info);
	}

	for (const FString& Key : GroupOrder)
	{
		const TArray<const FMacroControlInfo*>& Parts = GroupParts[Key];
		const FMacroControlInfo& First = *Parts[0];
		const float X = (static_cast<float>(First.Rect.X) + Pad) * U;
		const float Y = (static_cast<float>(First.Rect.Y) + Pad) * U;
		const float R = static_cast<float>(First.Rect.Z) * U * 0.5f;
		const FVector2f Centre(X + R, Y + R);
		const bool bKnob = First.Kind == TEXT("knob");

		Groups.Add({ bKnob, Centre, R });

		const float Ring = R * 0.92f;
		const float Inner = R * 0.38f;
		const float Q = UE_PI / 4.0f;

		// The centre press sits inside the wedges, so it has to be hit-tested first.
		for (const FMacroControlInfo* Info : Parts)
		{
			if (Info->Part != TEXT("press")) continue;
			FRegion& Region = Regions.AddDefaulted_GetRef();
			Region.Id = Info->Id;
			Region.Label = Info->Label;
			Region.Shape = ERegionShape::Disc;
			Region.Centre = Centre;
			Region.RadiusOuter = Inner;
		}

		for (const FMacroControlInfo* Info : Parts)
		{
			if (Info->Part == TEXT("press")) continue;
			FRegion& Region = Regions.AddDefaulted_GetRef();
			Region.Id = Info->Id;
			Region.Label = Info->Label;
			Region.Shape = ERegionShape::Wedge;
			Region.Centre = Centre;
			Region.RadiusInner = Inner;
			Region.RadiusOuter = Ring;
			// Screen space has +Y down, so increasing angles run clockwise.
			if (Info->Part == TEXT("ccw")) { Region.AngleStart = UE_PI * 0.5f; Region.AngleEnd = UE_PI * 1.5f; }
			else if (Info->Part == TEXT("cw")) { Region.AngleStart = -UE_PI * 0.5f; Region.AngleEnd = UE_PI * 0.5f; }
			else if (Info->Part == TEXT("up")) { Region.AngleStart = -3.0f * Q; Region.AngleEnd = -Q; }
			else if (Info->Part == TEXT("right")) { Region.AngleStart = -Q; Region.AngleEnd = Q; }
			else if (Info->Part == TEXT("down")) { Region.AngleStart = Q; Region.AngleEnd = 3.0f * Q; }
			else { Region.AngleStart = 3.0f * Q; Region.AngleEnd = 5.0f * Q; } // left
		}
	}

	// Recessed wells behind each key row, matching the web UI's plate.
	if (Regions.Num() > 0)
	{
		Wells.Add(FVector4f((1.3f + Pad) * U, (0.02f + Pad) * U, 6.2f * U, 1.16f * U));
		Wells.Add(FVector4f((0.2f + Pad) * U, (1.32f + Pad) * U, 7.3f * U, 1.16f * U));
		Wells.Add(FVector4f((0.2f + Pad) * U, (2.62f + Pad) * U, 5.35f * U, 1.16f * U));
	}

	BodySize = FVector2f(MaxX + Pad * U, MaxY + Pad * U);
}

const SMacroKeyboardDevice::FRegion* SMacroKeyboardDevice::HitTest(const FVector2f& Local) const
{
	for (const FRegion& Region : Regions)
	{
		switch (Region.Shape)
		{
		case ERegionShape::Key:
			if (Local.X >= Region.Pos.X && Local.X <= Region.Pos.X + Region.Size.X &&
				Local.Y >= Region.Pos.Y && Local.Y <= Region.Pos.Y + Region.Size.Y)
			{
				return &Region;
			}
			break;

		case ERegionShape::Disc:
			if (FVector2f::Distance(Local, Region.Centre) <= Region.RadiusOuter)
			{
				return &Region;
			}
			break;

		case ERegionShape::Wedge:
		{
			const FVector2f Delta = Local - Region.Centre;
			const float Dist = Delta.Size();
			if (Dist < Region.RadiusInner || Dist > Region.RadiusOuter)
			{
				break;
			}
			float Angle = FMath::Atan2(Delta.Y, Delta.X);
			while (Angle < Region.AngleStart)
			{
				Angle += 2.0f * UE_PI;
			}
			if (Angle <= Region.AngleEnd)
			{
				return &Region;
			}
			break;
		}
		}
	}
	return nullptr;
}

FReply SMacroKeyboardDevice::OnMouseButtonDown(const FGeometry& MyGeometry, const FPointerEvent& MouseEvent)
{
	if (MouseEvent.GetEffectingButton() != EKeys::LeftMouseButton)
	{
		return FReply::Unhandled();
	}
	const FVector2f Local = FVector2f(MyGeometry.AbsoluteToLocal(MouseEvent.GetScreenSpacePosition()));
	if (const FRegion* Region = HitTest(Local))
	{
		OnControlPicked.ExecuteIfBound(Region->Id);
		return FReply::Handled();
	}
	return FReply::Unhandled();
}

FReply SMacroKeyboardDevice::OnMouseMove(const FGeometry& MyGeometry, const FPointerEvent& MouseEvent)
{
	const FVector2f Local = FVector2f(MyGeometry.AbsoluteToLocal(MouseEvent.GetScreenSpacePosition()));
	const FRegion* Region = HitTest(Local);
	const FString NewHover = Region != nullptr ? Region->Id : FString();
	if (NewHover != HoveredControl)
	{
		HoveredControl = NewHover;
		Invalidate(EInvalidateWidgetReason::Paint);
	}
	return FReply::Unhandled();
}

void SMacroKeyboardDevice::OnMouseLeave(const FPointerEvent& MouseEvent)
{
	if (!HoveredControl.IsEmpty())
	{
		HoveredControl.Reset();
		Invalidate(EInvalidateWidgetReason::Paint);
	}
	SLeafWidget::OnMouseLeave(MouseEvent);
}

FCursorReply SMacroKeyboardDevice::OnCursorQuery(const FGeometry& MyGeometry, const FPointerEvent& CursorEvent) const
{
	return HoveredControl.IsEmpty() ? FCursorReply::Unhandled() : FCursorReply::Cursor(EMouseCursor::Hand);
}

FText SMacroKeyboardDevice::GetHoverTooltip() const
{
	if (HoveredControl.IsEmpty())
	{
		return LOCTEXT("DeviceTip", "Click a key, a knob zone or a joystick direction to edit what it does in the current context.");
	}
	for (const FRegion& Region : Regions)
	{
		if (Region.Id != HoveredControl)
		{
			continue;
		}
		const FString Summary = SummaryProvider ? SummaryProvider(Region.Id) : FString();
		return FText::Format(INVTEXT("{0} ({1})\n{2}"), ControlName(Region.Id, Region.Label), FText::FromString(Region.Id),
			Summary.IsEmpty() ? LOCTEXT("Unbound", "Unbound") : FText::FromString(Summary));
	}
	return FText::GetEmpty();
}

int32 SMacroKeyboardDevice::OnPaint(const FPaintArgs& Args, const FGeometry& AllottedGeometry, const FSlateRect& MyCullingRect,
	FSlateWindowElementList& OutDrawElements, int32 LayerId, const FWidgetStyle& InWidgetStyle, bool bParentEnabled) const
{
	const TSharedRef<FSlateFontMeasure> FontMeasure = FSlateApplication::Get().GetRenderer()->GetFontMeasureService();
	const FSlateResourceHandle Handle = FSlateApplication::Get().GetRenderer()->GetResourceHandle(*FAppStyle::GetBrush("WhiteBrush"));
	const FSlateRenderTransform& RenderTransform = AllottedGeometry.GetAccumulatedRenderTransform();
	const FString Selected = SelectedControl.Get(FString());
	const FString Pressed = PressedControl.Get(FString());

	auto Box = [&](const FSlateBrush& Brush, const FVector2f& Pos, const FVector2f& Size, const FLinearColor& Colour, int32 InLayer)
	{
		FSlateDrawElement::MakeBox(OutDrawElements, InLayer,
			AllottedGeometry.ToPaintGeometry(Size, FSlateLayoutTransform(Pos)), &Brush, ESlateDrawEffect::None, Colour);
	};

	auto Label = [&](const FString& Value, const FVector2f& Centre, const FSlateFontInfo& Font, const FLinearColor& Colour, int32 InLayer)
	{
		if (Value.IsEmpty())
		{
			return;
		}
		const FVector2D Measured = FontMeasure->Measure(Value, Font);
		const FVector2f Pos(Centre.X - static_cast<float>(Measured.X) * 0.5f, Centre.Y - static_cast<float>(Measured.Y) * 0.5f);
		FSlateDrawElement::MakeText(OutDrawElements, InLayer,
			AllottedGeometry.ToPaintGeometry(FVector2f(Measured), FSlateLayoutTransform(Pos)), Value, Font, ESlateDrawEffect::None, Colour);
	};

	// Filled ring segment; a full disc is just AngleStart 0 → 2π with RadiusInner 0.
	auto Fan = [&](const FVector2f& Centre, float RadiusInner, float RadiusOuter, float AngleStart, float AngleEnd, const FLinearColor& Colour, int32 InLayer)
	{
		const int32 Steps = FMath::Max(4, FMath::CeilToInt(FMath::Abs(AngleEnd - AngleStart) / (UE_PI / 24.0f)));
		const FColor Packed = Colour.ToFColorSRGB();
		TArray<FSlateVertex> Verts;
		TArray<SlateIndex> Indices;
		Verts.Reserve((Steps + 1) * 2);
		for (int32 Step = 0; Step <= Steps; ++Step)
		{
			const float Angle = FMath::Lerp(AngleStart, AngleEnd, static_cast<float>(Step) / static_cast<float>(Steps));
			const FVector2f Dir(FMath::Cos(Angle), FMath::Sin(Angle));
			Verts.Add(FSlateVertex::Make<ESlateVertexRounding::Disabled>(RenderTransform, Centre + Dir * RadiusInner, FVector2f::ZeroVector, Packed));
			Verts.Add(FSlateVertex::Make<ESlateVertexRounding::Disabled>(RenderTransform, Centre + Dir * RadiusOuter, FVector2f::ZeroVector, Packed));
			if (Step > 0)
			{
				const SlateIndex Base = static_cast<SlateIndex>((Step - 1) * 2);
				Indices.Append({ Base, static_cast<SlateIndex>(Base + 1), static_cast<SlateIndex>(Base + 2),
					static_cast<SlateIndex>(Base + 1), static_cast<SlateIndex>(Base + 3), static_cast<SlateIndex>(Base + 2) });
			}
		}
		FSlateDrawElement::MakeCustomVerts(OutDrawElements, InLayer, Handle, Verts, Indices, nullptr, 0, 0);
	};

	auto Triangle = [&](const FVector2f& A, const FVector2f& B, const FVector2f& C, const FLinearColor& Colour, int32 InLayer)
	{
		const FColor Packed = Colour.ToFColorSRGB();
		TArray<FSlateVertex> Verts;
		Verts.Add(FSlateVertex::Make<ESlateVertexRounding::Disabled>(RenderTransform, A, FVector2f::ZeroVector, Packed));
		Verts.Add(FSlateVertex::Make<ESlateVertexRounding::Disabled>(RenderTransform, B, FVector2f::ZeroVector, Packed));
		Verts.Add(FSlateVertex::Make<ESlateVertexRounding::Disabled>(RenderTransform, C, FVector2f::ZeroVector, Packed));
		const TArray<SlateIndex> Indices = { 0, 1, 2 };
		FSlateDrawElement::MakeCustomVerts(OutDrawElements, InLayer, Handle, Verts, Indices, nullptr, 0, 0);
	};

	// Rotation glyph: an open ring with an arrow head on its leading end.
	auto RotateIcon = [&](const FVector2f& Centre, float Radius, bool bClockwise, const FLinearColor& Colour, int32 InLayer)
	{
		const float Thickness = FMath::Max(2.0f, Radius * 0.34f);
		const float Start = bClockwise ? -UE_PI * 0.55f : UE_PI * 1.55f;
		const float End = bClockwise ? UE_PI * 1.05f : -UE_PI * 0.05f;
		Fan(Centre, Radius - Thickness * 0.5f, Radius + Thickness * 0.5f, FMath::Min(Start, End), FMath::Max(Start, End), Colour, InLayer);

		const FVector2f Normal(FMath::Cos(Start), FMath::Sin(Start));
		const FVector2f Tangent = bClockwise ? FVector2f(FMath::Sin(Start), -FMath::Cos(Start)) : FVector2f(-FMath::Sin(Start), FMath::Cos(Start));
		const float Head = Radius * 0.9f;
		Triangle(Centre + Normal * Radius + Tangent * Head,
			Centre + Normal * (Radius + Head * 0.75f),
			Centre + Normal * (Radius - Head * 0.75f), Colour, InLayer);
	};

	int32 Layer = LayerId;

	// ── body ──────────────────────────────────────────────────────────────────
	Box(BodyBrush(), FVector2f::ZeroVector, BodySize, ColBodyEdge, Layer);
	Box(BodyBrush(), FVector2f(1.5f, 1.5f), BodySize - FVector2f(3.0f, 3.0f), ColBody, Layer + 1);
	for (const FVector4f& Well : Wells)
	{
		Box(WellBrush(), FVector2f(Well.X, Well.Y), FVector2f(Well.Z, Well.W), ColWell, Layer + 2);
	}
	Layer += 3;

	// ── keys ──────────────────────────────────────────────────────────────────
	for (const FRegion& Region : Regions)
	{
		if (Region.Shape != ERegionShape::Key)
		{
			continue;
		}

		const bool bIsPressed = Region.Id == Pressed;
		const bool bIsSelected = Region.Id == Selected;
		const bool bIsHovered = Region.Id == HoveredControl;

		if (bIsSelected)
		{
			Box(CapBrush(), Region.Pos - FVector2f(2.0f, 2.0f), Region.Size + FVector2f(4.0f, 4.0f), FLinearColor::White, Layer);
		}
		Box(CapBrush(), Region.Pos, Region.Size, ColCapEdge, Layer + 1);
		Box(CapBrush(), Region.Pos + FVector2f(1.0f, 1.0f), Region.Size - FVector2f(2.0f, 2.0f), ColCap, Layer + 2);

		FLinearColor TopColour = Region.bAccent ? ColAccent : (Region.bGray ? ColGray : ColTop);
		if (bIsPressed)
		{
			TopColour = FLinearColor::White;
		}
		else if (bIsHovered)
		{
			TopColour = TopColour * 1.25f;
		}

		const FVector2f TopPos = Region.Pos + FVector2f(0.06f * U, 0.03f * U);
		const FVector2f TopSize = Region.Size - FVector2f(0.12f * U, 0.15f * U);
		Box(TopBrush(), TopPos, TopSize, TopColour, Layer + 3);

		const FString Summary = SummaryProvider ? SummaryProvider(Region.Id) : FString();
		const bool bUnbound = Summary.IsEmpty();
		const FLinearColor TextColour = bIsPressed || Region.bAccent ? ColInk : ColText;
		const FLinearColor SummaryColour = bIsPressed ? ColInk : (Region.bAccent ? ColAccentSummary : (bUnbound ? ColUnbound : ColSummary));

		const FSlateFontInfo SummaryFont = KeyFont(9, false);
		Label(Region.Label, FVector2f(TopPos.X + TopSize.X * 0.5f, TopPos.Y + TopSize.Y * 0.32f), KeyFont(13, true), TextColour, Layer + 4);
		const FString CapText = bUnbound ? LOCTEXT("Unbound", "Unbound").ToString() : CompactSummary(Summary);
		Label(FitToWidth(CapText, SummaryFont, TopSize.X - 8.0f, FontMeasure),
			FVector2f(TopPos.X + TopSize.X * 0.5f, TopPos.Y + TopSize.Y * 0.72f), SummaryFont, SummaryColour, Layer + 4);
	}
	Layer += 5;

	// ── knob and joystick ─────────────────────────────────────────────────────
	for (const FGroup& Group : Groups)
	{
		const float R = Group.Radius;
		Fan(Group.Centre, 0.0f, R * 0.98f, 0.0f, 2.0f * UE_PI, ColCapEdge, Layer);
		Fan(Group.Centre, 0.0f, R * 0.94f, 0.0f, 2.0f * UE_PI, ColWell, Layer + 1);
		Fan(Group.Centre, 0.0f, R * (Group.bKnob ? 0.78f : 0.62f), 0.0f, 2.0f * UE_PI, ColAccent, Layer + 2);
	}

	for (const FRegion& Region : Regions)
	{
		if (Region.Shape == ERegionShape::Key)
		{
			continue;
		}

		const bool bIsPressed = Region.Id == Pressed;
		const bool bIsSelected = Region.Id == Selected;
		const bool bIsHovered = Region.Id == HoveredControl;
		if (!bIsPressed && !bIsSelected && !bIsHovered)
		{
			continue;
		}

		const FLinearColor Overlay = bIsPressed ? FLinearColor(1.0f, 1.0f, 1.0f, 0.75f)
			: (bIsSelected ? FLinearColor(0.0f, 0.0f, 0.0f, 0.28f) : FLinearColor(1.0f, 1.0f, 1.0f, 0.16f));
		if (Region.Shape == ERegionShape::Disc)
		{
			Fan(Region.Centre, 0.0f, Region.RadiusOuter, 0.0f, 2.0f * UE_PI, Overlay, Layer + 3);
			if (bIsSelected)
			{
				Fan(Region.Centre, Region.RadiusOuter - 2.5f, Region.RadiusOuter, 0.0f, 2.0f * UE_PI, FLinearColor::White, Layer + 3);
			}
		}
		else
		{
			Fan(Region.Centre, Region.RadiusInner, Region.RadiusOuter, Region.AngleStart, Region.AngleEnd, Overlay, Layer + 3);
			if (bIsSelected)
			{
				// A white outline reads as "selected" on top of the accent-coloured cap.
				Fan(Region.Centre, Region.RadiusOuter - 2.5f, Region.RadiusOuter, Region.AngleStart, Region.AngleEnd, FLinearColor::White, Layer + 3);
				Fan(Region.Centre, Region.RadiusInner, Region.RadiusInner + 2.5f, Region.AngleStart, Region.AngleEnd, FLinearColor::White, Layer + 3);
				for (const float Edge : { Region.AngleStart, Region.AngleEnd })
				{
					const FVector2f Dir(FMath::Cos(Edge), FMath::Sin(Edge));
					const FVector2f Normal(-Dir.Y, Dir.X);
					Triangle(Region.Centre + Dir * Region.RadiusInner - Normal * 1.25f, Region.Centre + Dir * Region.RadiusOuter - Normal * 1.25f,
						Region.Centre + Dir * Region.RadiusOuter + Normal * 1.25f, FLinearColor::White, Layer + 3);
					Triangle(Region.Centre + Dir * Region.RadiusInner - Normal * 1.25f, Region.Centre + Dir * Region.RadiusOuter + Normal * 1.25f,
						Region.Centre + Dir * Region.RadiusInner + Normal * 1.25f, FLinearColor::White, Layer + 3);
				}
			}
		}
	}

	for (const FGroup& Group : Groups)
	{
		const float R = Group.Radius;
		if (Group.bKnob)
		{
			RotateIcon(FVector2f(Group.Centre.X - R * 0.55f, Group.Centre.Y), R * 0.16f, false, ColInk, Layer + 4);
			RotateIcon(FVector2f(Group.Centre.X + R * 0.55f, Group.Centre.Y), R * 0.16f, true, ColInk, Layer + 4);
		}
		else
		{
			const float Off = R * 0.72f;
			const float Half = R * 0.13f;
			const float High = R * 0.15f;
			const FVector2f C = Group.Centre;
			Triangle(FVector2f(C.X, C.Y - Off - High), FVector2f(C.X - Half, C.Y - Off + High), FVector2f(C.X + Half, C.Y - Off + High), ColInk, Layer + 4);
			Triangle(FVector2f(C.X, C.Y + Off + High), FVector2f(C.X - Half, C.Y + Off - High), FVector2f(C.X + Half, C.Y + Off - High), ColInk, Layer + 4);
			Triangle(FVector2f(C.X - Off - High, C.Y), FVector2f(C.X - Off + High, C.Y - Half), FVector2f(C.X - Off + High, C.Y + Half), ColInk, Layer + 4);
			Triangle(FVector2f(C.X + Off + High, C.Y), FVector2f(C.X + Off - High, C.Y - Half), FVector2f(C.X + Off - High, C.Y + Half), ColInk, Layer + 4);
		}

		// centre press cap
		Fan(Group.Centre, 0.0f, R * 0.30f, 0.0f, 2.0f * UE_PI, ColInk, Layer + 5);
		Fan(Group.Centre, 0.0f, R * 0.24f, 0.0f, 2.0f * UE_PI, Group.bKnob ? ColTop : ColCap, Layer + 6);
	}

	return Layer + 7;
}

#undef LOCTEXT_NAMESPACE
