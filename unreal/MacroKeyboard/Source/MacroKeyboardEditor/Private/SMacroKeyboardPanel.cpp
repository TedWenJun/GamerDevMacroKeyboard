// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "SMacroKeyboardPanel.h"

#include "MacroKeyboardEditorSettings.h"
#include "MacroKeyboardSubsystem.h"
#include "SMacroKeyboardDevice.h"
#include "Framework/Docking/TabManager.h"
#include "Framework/MultiBox/MultiBoxBuilder.h"
#include "Brushes/SlateRoundedBoxBrush.h"
#include "Misc/MessageDialog.h"
#include "Styling/AppStyle.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/Input/SSegmentedControl.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Colors/SColorBlock.h"
#include "Widgets/Colors/SColorPicker.h"
#include "Widgets/Input/SCheckBox.h"
#include "Widgets/Input/SComboBox.h"
#include "Widgets/Input/SComboButton.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Input/SInputKeySelector.h"
#include "Widgets/Input/SSlider.h"
#include "Widgets/Input/SSpinBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/Layout/SSeparator.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "MacroKeyboard"

const FName SMacroKeyboardPanel::TabId(TEXT("MacroKeyboardPanel"));
TWeakPtr<SMacroKeyboardPanel> SMacroKeyboardPanel::LivePanel;
FString SMacroKeyboardPanel::PendingSelection;

namespace
{
	constexpr float HighlightSeconds = 0.35f;
	/** The panel's own tab is not a context worth editing bindings for. */
	const TCHAR* PanelContext = TEXT("editor:MacroKeyboardPanel");

	/** Number of lighting effects the firmware has (mode 1..9, 9 = off). */
	constexpr int32 LightingModeCount = 9;

	/** "3 · Marquee": the effect's number as the firmware counts it, and its name. */
	FText LightingModeLabel(int32 Mode)
	{
		static const FText Names[LightingModeCount] = {
			LOCTEXT("LightMode1", "Solid"), LOCTEXT("LightMode2", "Flowing"), LOCTEXT("LightMode3", "Marquee"),
			LOCTEXT("LightMode4", "Breathing"), LOCTEXT("LightMode5", "Cycling breath"), LOCTEXT("LightMode6", "Tetris"),
			LOCTEXT("LightMode7", "Neon"), LOCTEXT("LightMode8", "Rainbow flow"), LOCTEXT("LightMode9", "Off"),
		};
		Mode = FMath::Clamp(Mode, 1, LightingModeCount);
		return FText::Format(INVTEXT("{0} · {1}"), FText::AsNumber(Mode), Names[Mode - 1]);
	}

	const TCHAR* KnownContexts[] = { TEXT("levelEditor"), TEXT("sequencer"), TEXT("animation"), TEXT("animBlueprint"), TEXT("pie"), TEXT("simulate") };

	FText ContextDisplayName(const FString& Context)
	{
		if (Context.IsEmpty()) return LOCTEXT("ContextAll", "All contexts (generic)");
		if (Context == TEXT("levelEditor")) return LOCTEXT("ContextLevel", "Level Editor");
		if (Context == TEXT("sequencer")) return LOCTEXT("ContextSequencer", "Sequencer");
		if (Context == TEXT("animation")) return LOCTEXT("ContextAnim", "Animation Editor");
		if (Context == TEXT("animBlueprint")) return LOCTEXT("ContextAnimBP", "Animation Blueprint");
		if (Context == TEXT("pie")) return LOCTEXT("ContextPie", "Playing in Editor");
		if (Context == TEXT("simulate")) return LOCTEXT("ContextSimulate", "Simulate");
		return FText::FromString(Context);
	}

	FText ActionDisplayName(EMacroEditorAction Action)
	{
		switch (Action)
		{
		case EMacroEditorAction::Chord: return LOCTEXT("ActionChord", "Editor shortcut");
		case EMacroEditorAction::UICommand: return LOCTEXT("ActionCommand", "Editor command (by name)");
		case EMacroEditorAction::ConsoleCommand: return LOCTEXT("ActionConsole", "Console command");
		case EMacroEditorAction::TimelineScrub: return LOCTEXT("ActionScrub", "Timeline scrub");
		case EMacroEditorAction::TimelinePlayPause: return LOCTEXT("ActionPlay", "Timeline play / pause");
		default: return LOCTEXT("ActionNone", "None (block)");
		}
	}

	// ── look ───────────────────────────────────────────────────────────────
	// Cards and chips share the device's palette (MacroHub's web UI), so the chrome and the plate read as one tool.

	FLinearColor UiHex(const TCHAR* Value) { return FLinearColor(FColor::FromHex(Value)); }

	const FLinearColor UiCard = UiHex(TEXT("#1b1c1f"));
	const FLinearColor UiCardEdge = UiHex(TEXT("#2b2e33"));
	const FLinearColor UiChip = UiHex(TEXT("#2c2f35"));
	const FLinearColor UiMuted = UiHex(TEXT("#8b9099"));
	const FLinearColor UiAccent = UiHex(TEXT("#f5c400"));
	const FLinearColor UiOk = UiHex(TEXT("#34c77b"));
	const FLinearColor UiWarn = UiHex(TEXT("#ff9f43"));
	const FLinearColor UiOff = UiHex(TEXT("#6b7079"));

	/** Width of the label column in the binding form, so every value starts at the same x. */
	constexpr float LabelWidth = 120.0f;
	/** Width of wide value widgets (combos, text boxes) in the binding form. */
	constexpr float FieldWidth = 240.0f;
	constexpr float LightingWidth = 260.0f;

	const FSlateBrush* CardBrush() { static const FSlateRoundedBoxBrush B(UiCard, 8.0f, UiCardEdge, 1.0f); return &B; }
	const FSlateBrush* ChipBrush() { static const FSlateRoundedBoxBrush B(UiChip, 10.0f); return &B; }
	const FSlateBrush* SwatchBrush() { static const FSlateRoundedBoxBrush B(FLinearColor::Transparent, 4.0f, UiCardEdge, 1.0f); return &B; }

	FSlateFontInfo UiFont(int32 Size, bool bBold = false)
	{
		FSlateFontInfo Font = FAppStyle::Get().GetFontStyle(bBold ? TEXT("NormalFontBold") : TEXT("NormalFont"));
		Font.Size = Size;
		return Font;
	}

	TSharedRef<SWidget> Card(const TSharedRef<SWidget>& Content, const FMargin& Padding = FMargin(14.0f))
	{
		return SNew(SBorder).BorderImage(CardBrush()).Padding(Padding)[Content];
	}

	TSharedRef<SWidget> CardTitle(const FText& Text)
	{
		return SNew(STextBlock).Text(Text).Font(UiFont(11, true));
	}

	/** Small caps-like heading between groups of rows. */
	TSharedRef<SWidget> SectionTitle(const FText& Text)
	{
		return SNew(STextBlock).Text(Text).Font(UiFont(9, true)).ColorAndOpacity(UiAccent);
	}

	TSharedRef<SWidget> Muted(const FText& Text)
	{
		return SNew(STextBlock).Text(Text).ColorAndOpacity(UiMuted).AutoWrapText(true);
	}

	/** Lighting card row header: name on the left, current value on the right. */
	TSharedRef<SWidget> LightLabel(const FText& Label, const TAttribute<FText>& Value = TAttribute<FText>())
	{
		return SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(1.0f)[SNew(STextBlock).Text(Label).ColorAndOpacity(UiMuted)]
			+ SHorizontalBox::Slot().AutoWidth()[SNew(STextBlock).Text(Value)];
	}

}

void SMacroKeyboardPanel::Construct(const FArguments& InArgs)
{
	RefreshContextOptions();

	if (UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
	{
		EventHandle = Subsystem->OnControlEvent.AddSP(this, &SMacroKeyboardPanel::HandleControlEvent);
		DeviceHandle = Subsystem->OnDeviceChanged.AddSP(this, &SMacroKeyboardPanel::RebuildDevice);
	}
	RegisterActiveTimer(0.1f, FWidgetActiveTimerDelegate::CreateSP(this, &SMacroKeyboardPanel::TickHighlight));

	ChildSlot
	[
		SNew(SVerticalBox)

		// ── toolbar: connection, edited context, links ───────────────────────
		+ SVerticalBox::Slot().AutoHeight().Padding(12, 10, 12, 6)
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
			[
				SNew(SBorder)
				.BorderImage(ChipBrush())
				.Padding(FMargin(10, 4))
				.ToolTipText_Lambda([]()
				{
					const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
					return Subsystem != nullptr ? FText::FromString(Subsystem->DescribeConnection()) : FText::GetEmpty();
				})
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(0, 0, 6, 0)
					[
						SNew(SImage)
						.DesiredSizeOverride(FVector2D(8.0f, 8.0f))
						.Image(FAppStyle::GetBrush("Icons.FilledCircle"))
						.ColorAndOpacity_Lambda([]()
						{
							const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
							if (Subsystem == nullptr || !Subsystem->IsConnected()) return FSlateColor(UiOff);
							return FSlateColor(Subsystem->IsPadConnected() ? UiOk : UiWarn);
						})
					]
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
					[
						SNew(STextBlock)
						.Text_Lambda([]()
						{
							const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
							if (Subsystem == nullptr) return LOCTEXT("NoSubsystem", "MacroKeyboard is not initialised");
							if (!Subsystem->IsConnected()) return LOCTEXT("HubOffline", "MacroHub not connected");
							return Subsystem->IsPadConnected()
								? LOCTEXT("PadOnline", "MacroHub connected · pad online")
								: LOCTEXT("PadOffline", "MacroHub connected · pad not connected");
						})
					]
				]
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(20, 0, 8, 0)
			[
				SNew(STextBlock).Text(LOCTEXT("EditContext", "Editing context")).ColorAndOpacity(UiMuted)
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
			[
				SNew(SBox).MinDesiredWidth(200.0f)
				[
					SNew(SComboBox<TSharedPtr<FString>>)
					.OptionsSource(&ContextOptions)
					.IsEnabled_Lambda([this]() { return !bFollowLiveContext; })
					.OnGenerateWidget_Lambda([](TSharedPtr<FString> Item)
					{
						return SNew(STextBlock).Text(ContextDisplayName(*Item));
					})
					.OnSelectionChanged_Lambda([this](TSharedPtr<FString> Item, ESelectInfo::Type)
					{
						if (Item.IsValid())
						{
							EditedContext = *Item;
							RebuildDevice();
							RebuildDetails();
						}
					})
					[
						SNew(STextBlock).Text_Lambda([this]() { return ContextDisplayName(ActiveContext()); })
					]
				]
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(10, 0, 0, 0)
			[
				SNew(SCheckBox)
				.ToolTipText(LOCTEXT("FollowTip", "The edited context follows the editor’s focus; untick to choose one yourself"))
				.IsChecked_Lambda([this]() { return bFollowLiveContext ? ECheckBoxState::Checked : ECheckBoxState::Unchecked; })
				.OnCheckStateChanged_Lambda([this](ECheckBoxState State)
				{
					bFollowLiveContext = State == ECheckBoxState::Checked;
					RebuildDevice();
					RebuildDetails();
				})
				.Content()[SNew(STextBlock).Text(LOCTEXT("Follow", "Follow editor"))]
			]
			+ SHorizontalBox::Slot().FillWidth(1.0f).MinWidth(12.0f)[SNullWidget::NullWidget]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(4, 0, 0, 0)
			[
				SNew(SButton)
				.Visibility(InArgs._ShowOpenInWindow ? EVisibility::Visible : EVisibility::Collapsed)
				.Text(LOCTEXT("OpenPanelWindow", "Open in its own window"))
				.ToolTipText(LOCTEXT("OpenPanelWindowTip", "Open this panel as a separate editor window"))
				.OnClicked_Lambda([]()
				{
					FGlobalTabmanager::Get()->TryInvokeTab(SMacroKeyboardPanel::TabId);
					return FReply::Handled();
				})
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(4, 0, 0, 0)
			[
				SNew(SButton)
				.Text(LOCTEXT("OpenHub", "MacroHub setup page"))
				.ToolTipText(LOCTEXT("OpenHubTip", "Open MacroHub’s device and forwarding settings in the browser"))
				.OnClicked_Lambda([]()
				{
					FPlatformProcess::LaunchURL(TEXT("http://127.0.0.1:17900/"), nullptr, nullptr);
					return FReply::Handled();
				})
			]
		]

		// ── device + lighting, binding underneath at the same width ───────────
		+ SVerticalBox::Slot().FillHeight(1.0f)
		[
			SNew(SScrollBox)
			+ SScrollBox::Slot().Padding(12, 4, 12, 12)
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth()
				[
					SNew(SVerticalBox)
					+ SVerticalBox::Slot().AutoHeight()
					[
						SNew(SHorizontalBox)
						+ SHorizontalBox::Slot().AutoWidth()
						[
							Card(
								SAssignNew(Device, SMacroKeyboardDevice)
								.SelectedControl_Lambda([this]() { return SelectedControl; })
								.PressedControl_Lambda([this]() { return PressedControl; })
								.OnControlPicked_Lambda([this](FString ControlId)
								{
									SelectedControl = MoveTemp(ControlId);
									RebuildDetails();
									if (Device.IsValid())
									{
										Device->Invalidate(EInvalidateWidgetReason::Paint);
									}
								}),
								FMargin(12.0f))
						]
						+ SHorizontalBox::Slot().AutoWidth().Padding(10, 0, 0, 0)
						[
							BuildLighting()
						]
					]
					+ SVerticalBox::Slot().AutoHeight().Padding(0, 10, 0, 0)
					[
						Card(SAssignNew(DetailsBox, SVerticalBox), FMargin(16.0f, 14.0f))
					]
				]
			]
		]
	];

	LivePanel = SharedThis(this);
	if (!PendingSelection.IsEmpty())
	{
		SelectedControl = MoveTemp(PendingSelection);
		PendingSelection.Reset();
	}

	RebuildDevice();
	RebuildDetails();
}

void SMacroKeyboardPanel::SelectControl(const FString& ControlId)
{
	if (const TSharedPtr<SMacroKeyboardPanel> Panel = LivePanel.Pin())
	{
		Panel->SelectedControl = ControlId;
		Panel->RebuildDetails();
		if (Panel->Device.IsValid())
		{
			Panel->Device->Invalidate(EInvalidateWidgetReason::Paint);
		}
		return;
	}
	PendingSelection = ControlId; // the tab is still opening; it picks this up when it builds
}

SMacroKeyboardPanel::~SMacroKeyboardPanel()
{
	if (UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
	{
		Subsystem->OnControlEvent.Remove(EventHandle);
		Subsystem->OnDeviceChanged.Remove(DeviceHandle);
	}
}

FString SMacroKeyboardPanel::ActiveContext() const
{
	if (bFollowLiveContext)
	{
		if (const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
		{
			// While this panel has focus the live context is the panel's own tab, which nobody wants to
			// bind against - keep showing the editor the user came from instead.
			const FString& Live = Subsystem->GetReportedContext();
			if (!Live.IsEmpty() && Live != PanelContext)
			{
				return Live;
			}
		}
		if (!LastLiveContext.IsEmpty())
		{
			return LastLiveContext;
		}
	}
	return EditedContext;
}

void SMacroKeyboardPanel::RefreshContextOptions()
{
	ContextOptions.Reset();
	ContextOptions.Add(MakeShared<FString>(FString())); // all contexts
	for (const TCHAR* Context : KnownContexts)
	{
		ContextOptions.Add(MakeShared<FString>(FString(Context)));
	}
	// contexts the user already wrote bindings for (e.g. editor:SomeTab)
	for (const FMacroKeyboardBinding& Binding : UMacroKeyboardEditorSettings::Get().Bindings)
	{
		const bool bKnown = ContextOptions.ContainsByPredicate([&Binding](const TSharedPtr<FString>& Item) { return *Item == Binding.Context; });
		if (!bKnown)
		{
			ContextOptions.Add(MakeShared<FString>(Binding.Context));
		}
	}
}

FMacroContextLighting* SMacroKeyboardPanel::FindLightingEntry(bool bContextOnly) const
{
	UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
	const FString Context = ActiveContext();
	FMacroContextLighting* Shared = nullptr;
	for (FMacroContextLighting& Entry : Settings->ContextLighting)
	{
		if (Entry.Context == Context)
		{
			return &Entry;
		}
		if (Entry.Context.IsEmpty() && Shared == nullptr)
		{
			Shared = &Entry;
		}
	}
	return bContextOnly ? nullptr : Shared;
}

FMacroLightingSpec SMacroKeyboardPanel::EditedLighting() const
{
	const FMacroContextLighting* Entry = FindLightingEntry();
	return Entry != nullptr ? Entry->Lighting : FMacroLightingSpec();
}

void SMacroKeyboardPanel::SetLighting(TFunctionRef<void(FMacroLightingSpec&)> Edit)
{
	UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
	FMacroContextLighting* Entry = FindLightingEntry();
	if (Entry == nullptr)
	{
		// No entry yet: start from the defaults and make it the shared one.
		Entry = &Settings->ContextLighting.AddDefaulted_GetRef();
	}
	Edit(Entry->Lighting);
	Settings->SaveConfig();
	PushLighting();
}

void SMacroKeyboardPanel::PushLighting()
{
	UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
	if (Subsystem == nullptr)
	{
		return;
	}
	const UMacroKeyboardEditorSettings& Settings = UMacroKeyboardEditorSettings::Get();
	if (!Settings.bDriveLighting)
	{
		Subsystem->ResetLighting();
		return;
	}
	Subsystem->SetLighting(EditedLighting());
}

TSharedRef<SWidget> SMacroKeyboardPanel::BuildLighting()
{
	// Same five controls as MacroHub's own lighting panel, so both front-ends behave the same.
	// Everything below the header is dimmed while the editor does not drive the backlight.
	const TSharedRef<SVerticalBox> Controls = SNew(SVerticalBox)
		.IsEnabled_Lambda([]() { return UMacroKeyboardEditorSettings::Get().bDriveLighting; });

	// mode
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 4)[LightLabel(LOCTEXT("LightMode", "Mode"))];
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 12)
	[
		SNew(SComboButton)
		.OnGetMenuContent_Lambda([this]()
		{
			FMenuBuilder Menu(true, nullptr);
			for (int32 Mode = 1; Mode <= LightingModeCount; ++Mode)
			{
				Menu.AddMenuEntry(LightingModeLabel(Mode),
					FText::GetEmpty(), FSlateIcon(),
					FUIAction(FExecuteAction::CreateLambda([this, Mode]()
					{
						SetLighting([Mode](FMacroLightingSpec& Spec) { Spec.Mode = Mode; });
					})));
			}
			return Menu.MakeWidget();
		})
		.ButtonContent()
		[
			SNew(STextBlock).Text_Lambda([this]() { return LightingModeLabel(EditedLighting().Mode); })
		]
	];

	// brightness
	Controls->AddSlot().AutoHeight()
	[
		LightLabel(LOCTEXT("LightBright", "Brightness"),
			TAttribute<FText>::CreateLambda([this]() { return FText::Format(LOCTEXT("LightBrightFmt", "{0} / 6"), EditedLighting().Brightness); }))
	];
	Controls->AddSlot().AutoHeight().Padding(0, 2, 0, 10)
	[
		SNew(SSlider)
		.MinValue(1.0f).MaxValue(6.0f).StepSize(1.0f).MouseUsesStep(true)
		.Value_Lambda([this]() { return static_cast<float>(EditedLighting().Brightness); })
		.OnValueChanged_Lambda([this](float Value)
		{
			SetLighting([Value](FMacroLightingSpec& Spec) { Spec.Brightness = FMath::RoundToInt(Value); });
		})
	];

	// colour
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 4)
	[
		LightLabel(LOCTEXT("LightColor", "Colour"),
			TAttribute<FText>::CreateLambda([this]() { return FText::FromString(TEXT("#") + EditedLighting().Color.ToFColor(true).ToHex().Left(6)); }))
	];
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 12)
	[
		SNew(SBorder)
		.BorderImage(SwatchBrush())
		.Padding(1.0f)
		.ToolTipText(LOCTEXT("LightColorTip", "Click to pick a colour"))
		[
			SNew(SColorBlock)
			.Color_Lambda([this]() { return EditedLighting().Color; })
			.CornerRadius(FVector4(3.0f, 3.0f, 3.0f, 3.0f))
			.Size(FVector2D(LightingWidth - 30.0f, 22.0f))
			.OnMouseButtonDown_Lambda([this](const FGeometry&, const FPointerEvent& Event) -> FReply
			{
				if (Event.GetEffectingButton() != EKeys::LeftMouseButton)
				{
					return FReply::Unhandled();
				}
				FColorPickerArgs Args;
				Args.bIsModal = false;
				Args.bUseAlpha = false;
				Args.InitialColor = EditedLighting().Color;
				Args.OnColorCommitted = FOnLinearColorValueChanged::CreateLambda([this](FLinearColor NewColor)
				{
					SetLighting([NewColor](FMacroLightingSpec& Spec) { Spec.Color = NewColor; });
				});
				OpenColorPicker(Args);
				return FReply::Handled();
			})
		]
	];

	// speed
	Controls->AddSlot().AutoHeight()
	[
		LightLabel(LOCTEXT("LightSpeed", "Speed"),
			TAttribute<FText>::CreateLambda([this]() { return FText::Format(LOCTEXT("LightSpeedFmt", "{0} / 5"), EditedLighting().Speed); }))
	];
	Controls->AddSlot().AutoHeight().Padding(0, 2, 0, 10)
	[
		SNew(SSlider)
		.MinValue(0.0f).MaxValue(5.0f).StepSize(1.0f).MouseUsesStep(true)
		.Value_Lambda([this]() { return static_cast<float>(EditedLighting().Speed); })
		.OnValueChanged_Lambda([this](float Value)
		{
			SetLighting([Value](FMacroLightingSpec& Spec) { Spec.Speed = FMath::RoundToInt(Value); });
		})
	];

	// direction
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 4)[LightLabel(LOCTEXT("LightDir", "Direction"))];
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 12)
	[
		SNew(SSegmentedControl<int32>)
		.Value_Lambda([this]() { return EditedLighting().Direction; })
		.OnValueChanged_Lambda([this](int32 Value) { SetLighting([Value](FMacroLightingSpec& Spec) { Spec.Direction = Value; }); })
		+ SSegmentedControl<int32>::Slot(0).Text(LOCTEXT("LightDirCw", "Clockwise"))
		+ SSegmentedControl<int32>::Slot(1).Text(LOCTEXT("LightDirCcw", "Anticlockwise"))
	];

	// per-context
	Controls->AddSlot().AutoHeight().Padding(0, 0, 0, 8)[SNew(SSeparator).Thickness(1.0f)];
	Controls->AddSlot().AutoHeight()
	[
		SNew(SCheckBox)
		.ToolTipText(LOCTEXT("LightPerContextTip", "Gives this context its own lighting, applied whenever it comes to the front"))
		.IsChecked_Lambda([this]() { return FindLightingEntry(true) != nullptr ? ECheckBoxState::Checked : ECheckBoxState::Unchecked; })
		.OnCheckStateChanged_Lambda([this](ECheckBoxState State)
		{
			UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
			const FString Context = ActiveContext();
			if (State == ECheckBoxState::Checked)
			{
				if (FindLightingEntry(true) == nullptr)
				{
					FMacroContextLighting Entry;
					Entry.Context = Context;
					Entry.Lighting = EditedLighting();
					Settings->ContextLighting.Add(Entry);
				}
			}
			else
			{
				Settings->ContextLighting.RemoveAll([&Context](const FMacroContextLighting& Entry)
				{
					return !Entry.Context.IsEmpty() && Entry.Context == Context;
				});
			}
			Settings->SaveConfig();
			PushLighting();
		})
		.Content()[SNew(STextBlock).Text(LOCTEXT("LightPerContext", "Per context"))]
	];

	return SNew(SBox)
		.WidthOverride(LightingWidth)
		[
			Card(
				SNew(SVerticalBox)
				+ SVerticalBox::Slot().AutoHeight().Padding(0, 0, 0, 12)
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().FillWidth(1.0f).VAlign(VAlign_Center)
					[
						CardTitle(LOCTEXT("LightTitle", "Pad lighting"))
					]
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
					[
						SNew(SCheckBox)
						.ToolTipText(LOCTEXT("LightDriveTip", "When on, the editor sets the pad’s backlight per context; when off, MacroHub controls it again"))
						.IsChecked_Lambda([]()
						{
							return UMacroKeyboardEditorSettings::Get().bDriveLighting ? ECheckBoxState::Checked : ECheckBoxState::Unchecked;
						})
						.OnCheckStateChanged_Lambda([this](ECheckBoxState State)
						{
							UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
							Settings->bDriveLighting = State == ECheckBoxState::Checked;
							Settings->SaveConfig();
							PushLighting();
						})
						.Content()[SNew(STextBlock).Text(LOCTEXT("LightDrive", "Take over"))]
					]
				]
				+ SVerticalBox::Slot().AutoHeight()[Controls]
				+ SVerticalBox::Slot().FillHeight(1.0f)[SNullWidget::NullWidget]
				+ SVerticalBox::Slot().AutoHeight().Padding(0, 8, 0, 0)
				[
					SNew(STextBlock)
					.Text_Lambda([this]()
					{
						if (!UMacroKeyboardEditorSettings::Get().bDriveLighting)
						{
							return LOCTEXT("LightOff", "Not taken over: MacroHub controls the backlight");
						}
						return FindLightingEntry(true) != nullptr
							? FText::Format(LOCTEXT("LightOwnFmt", "Lighting for “{0}”"), ContextDisplayName(ActiveContext()))
							: LOCTEXT("LightShared", "Shared by all contexts");
					})
					.ColorAndOpacity(UiMuted)
					.AutoWrapText(true)
				])
		];
}

void SMacroKeyboardPanel::RebuildDevice()
{
	if (!Device.IsValid())
	{
		return;
	}

	// The device draws itself from the Hub's layout and asks us for each control's binding text.
	Device->SetSummaryProvider([this](const FString& ControlId) { return BindingSummary(ControlId); });

	const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
	Device->SetControls(Subsystem != nullptr ? Subsystem->GetControls() : TArray<FMacroControlInfo>());

	// The hint under the device depends on whether a layout arrived, so refresh it when one does.
	if (SelectedControl.IsEmpty())
	{
		RebuildDetails();
	}
}

FString SMacroKeyboardPanel::BindingSummary(const FString& ControlId) const
{
	const FString Context = ActiveContext();
	auto Label = [](const FMacroKeyboardBinding& Binding)
	{
		return Binding.DisplayName.IsEmpty() ? Binding.Describe() : Binding.DisplayName;
	};
	if (const FMacroKeyboardBinding* Binding = FindBinding(Context, ControlId))
	{
		return Label(*Binding);
	}
	if (!Context.IsEmpty())
	{
		if (const FMacroKeyboardBinding* Generic = FindBinding(FString(), ControlId))
		{
			return FString::Printf(TEXT("↳ %s"), *Label(*Generic)); // inherited from the generic binding
		}
	}
	return FString(); // unbound: the device draws its own "Unbound" in the editor language
}

FMacroKeyboardBinding* SMacroKeyboardPanel::FindBinding(const FString& Context, const FString& Control) const
{
	UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
	for (FMacroKeyboardBinding& Binding : Settings->Bindings)
	{
		if (Binding.Context == Context && Binding.Control == Control)
		{
			return &Binding;
		}
	}
	return nullptr;
}

FMacroKeyboardBinding& SMacroKeyboardPanel::EnsureBinding()
{
	const FString Context = ActiveContext();
	if (FMacroKeyboardBinding* Existing = FindBinding(Context, SelectedControl))
	{
		return *Existing;
	}
	UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
	FMacroKeyboardBinding NewBinding;
	NewBinding.Context = Context;
	NewBinding.Control = SelectedControl;
	NewBinding.Action = EMacroEditorAction::Chord;
	const int32 Index = Settings->Bindings.Add(MoveTemp(NewBinding));
	return Settings->Bindings[Index];
}

void SMacroKeyboardPanel::RemoveBinding()
{
	UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
	const FString Context = ActiveContext();
	Settings->Bindings.RemoveAll([&](const FMacroKeyboardBinding& Binding)
	{
		return Binding.Context == Context && Binding.Control == SelectedControl;
	});
	SaveSettings();
}

void SMacroKeyboardPanel::SaveSettings()
{
	GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
	RefreshContextOptions();
	RebuildDevice();
	RebuildDetails();
}

void SMacroKeyboardPanel::RebuildDetails()
{
	if (!DetailsBox.IsValid())
	{
		return;
	}
	DetailsBox->ClearChildren();

	// A two-column form: labels in a fixed column, so every value starts at the same x.
	auto AddRow = [this](const FText& Label, const TSharedRef<SWidget>& Value, const FText& Tip = FText::GetEmpty())
	{
		DetailsBox->AddSlot().AutoHeight().Padding(0, 3)
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
			[
				SNew(SBox).WidthOverride(LabelWidth)
				[
					SNew(STextBlock).Text(Label).ToolTipText(Tip).ColorAndOpacity(UiMuted)
				]
			]
			+ SHorizontalBox::Slot().FillWidth(1.0f).VAlign(VAlign_Center).HAlign(HAlign_Left)
			[
				Value
			]
		];
	};
	auto AddSection = [this](const FText& Title)
	{
		DetailsBox->AddSlot().AutoHeight().Padding(0, 14, 0, 4)[SectionTitle(Title)];
	};
	auto Field = [](const TSharedRef<SWidget>& Widget, float Width = FieldWidth) -> TSharedRef<SWidget>
	{
		return SNew(SBox).WidthOverride(Width)[Widget];
	};

	if (SelectedControl.IsEmpty())
	{
		const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
		const bool bHasDevice = Subsystem != nullptr && Subsystem->GetControls().Num() > 0;
		DetailsBox->AddSlot().AutoHeight()[CardTitle(LOCTEXT("BindingTitle", "Key settings"))];
		DetailsBox->AddSlot().AutoHeight().Padding(0, 8, 0, 0)
		[
			Muted(bHasDevice
				? LOCTEXT("PickControl", "Click a key, a knob zone or a joystick direction above to edit it; pressing a physical key highlights it.")
				: LOCTEXT("NoDevice", "Waiting for MacroHub to send the pad layout… (make sure MacroHub is running)"))
		];
		return;
	}

	const FString Context = ActiveContext();
	FMacroKeyboardBinding* Binding = FindBinding(Context, SelectedControl);

	FString ControlLabel;
	if (const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
	{
		if (const FMacroControlInfo* Info = Subsystem->GetControls().FindByPredicate(
			[this](const FMacroControlInfo& Control) { return Control.Id == SelectedControl; }))
		{
			ControlLabel = Info->Label;
		}
	}

	// ── header: which control, where, and the way out ─────────────────────────
	DetailsBox->AddSlot().AutoHeight()
	[
		SNew(SHorizontalBox)
		+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(0, 0, 10, 0)
		[
			SNew(SBorder)
			.BorderImage(ChipBrush())
			.Padding(FMargin(8, 2))
			[
				SNew(STextBlock).Text(FText::FromString(SelectedControl)).Font(UiFont(9, true)).ColorAndOpacity(UiAccent)
			]
		]
		+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(0, 0, 10, 0)
		[
			CardTitle(MacroKeyboardDevice::ControlName(SelectedControl, ControlLabel))
		]
		+ SHorizontalBox::Slot().FillWidth(1.0f).VAlign(VAlign_Center)
		[
			SNew(STextBlock)
			.Text(FText::Format(LOCTEXT("EditingFmt", "in “{0}”"), ContextDisplayName(Context)))
			.ColorAndOpacity(UiMuted)
		]
		+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
		[
			SNew(SButton)
			.Text(LOCTEXT("ClearBinding", "Remove binding"))
			.ToolTipText(LOCTEXT("ClearBindingTip", "Removes this context’s binding; if there is an “All contexts” binding, the control inherits it again"))
			.IsEnabled(Binding != nullptr)
			.OnClicked_Lambda([this]() { RemoveBinding(); return FReply::Handled(); })
		]
	];

	// Say where the behaviour comes from when this context has no binding of its own.
	if (Binding == nullptr && !Context.IsEmpty())
	{
		if (const FMacroKeyboardBinding* Generic = FindBinding(FString(), SelectedControl))
		{
			DetailsBox->AddSlot().AutoHeight().Padding(0, 8, 0, 0)
			[
				Muted(FText::Format(LOCTEXT("InheritedFmt", "Currently inherited from “All contexts”: {0}. Pick an action below to override it for this context only."),
					FText::FromString(MacroKeyboardDevice::CompactSummary(Generic->Describe()))))
			];
		}
	}

	// ── action ───────────────────────────────────────────────────────────────
	AddSection(LOCTEXT("SectionAction", "Action"));
	AddRow(LOCTEXT("ActionLabel", "Action type"), Field(
		SNew(SComboButton)
		.OnGetMenuContent_Lambda([this]()
		{
			FMenuBuilder Menu(true, nullptr);
			const EMacroEditorAction Actions[] = { EMacroEditorAction::Chord, EMacroEditorAction::UICommand,
				EMacroEditorAction::ConsoleCommand, EMacroEditorAction::TimelineScrub,
				EMacroEditorAction::TimelinePlayPause, EMacroEditorAction::None };
			for (EMacroEditorAction Action : Actions)
			{
				Menu.AddMenuEntry(ActionDisplayName(Action), FText::GetEmpty(), FSlateIcon(),
					FUIAction(FExecuteAction::CreateLambda([this, Action]()
					{
						EnsureBinding().Action = Action;
						SaveSettings();
					})));
			}
			return Menu.MakeWidget();
		})
		.ButtonContent()
		[
			SNew(STextBlock).Text_Lambda([this, Context]()
			{
				const FMacroKeyboardBinding* Current = FindBinding(Context, SelectedControl);
				return Current != nullptr ? ActionDisplayName(Current->Action) : LOCTEXT("Unbound", "Unbound");
			})
		]));

	if (Binding == nullptr)
	{
		DetailsBox->AddSlot().AutoHeight().Padding(LabelWidth, 4, 0, 0)
		[
			Muted(LOCTEXT("PickAction", "Pick an action to create a binding for this context."))
		];
		AddRestoreDefaults();
		return;
	}

	const EMacroEditorAction Action = Binding->Action;

	if (Action == EMacroEditorAction::Chord)
	{
		AddRow(LOCTEXT("ChordLabel", "Shortcut"), Field(
			SNew(SInputKeySelector)
			.SelectedKey(Binding->Chord)
			.AllowModifierKeys(true)
			.OnKeySelected_Lambda([this](const FInputChord& NewChord)
			{
				EnsureBinding().Chord = NewChord;
				SaveSettings();
			}), 160.0f),
			LOCTEXT("ChordTip", "Click, then press the key combination"));
	}
	else if (Action == EMacroEditorAction::UICommand)
	{
		AddRow(LOCTEXT("CommandCtxLabel", "Binding context"), Field(
			SNew(SEditableTextBox)
			.Text(FText::FromName(Binding->CommandContext))
			.HintText(LOCTEXT("CommandCtxHint", "e.g. Sequencer"))
			.OnTextCommitted_Lambda([this](const FText& Text, ETextCommit::Type)
			{
				EnsureBinding().CommandContext = FName(*Text.ToString());
				SaveSettings();
			})));
		AddRow(LOCTEXT("CommandNameLabel", "Command name"), Field(
			SNew(SEditableTextBox)
			.Text(FText::FromName(Binding->CommandName))
			.HintText(LOCTEXT("CommandNameHint", "e.g. TogglePlay"))
			.OnTextCommitted_Lambda([this](const FText& Text, ETextCommit::Type)
			{
				EnsureBinding().CommandName = FName(*Text.ToString());
				SaveSettings();
			})));
	}
	else if (Action == EMacroEditorAction::ConsoleCommand)
	{
		AddRow(LOCTEXT("ConsoleLabel", "Console command"), Field(
			SNew(SEditableTextBox)
			.Text(FText::FromString(Binding->ConsoleCommand))
			.HintText(LOCTEXT("ConsoleHint", "e.g. stat fps"))
			.OnTextCommitted_Lambda([this](const FText& Text, ETextCommit::Type)
			{
				EnsureBinding().ConsoleCommand = Text.ToString();
				SaveSettings();
			}), 360.0f));
	}
	else if (Action == EMacroEditorAction::TimelineScrub)
	{
		AddRow(LOCTEXT("FramesLabel", "Frames per step"), Field(
			SNew(SSpinBox<float>)
			.MinValue(1.0f).MaxValue(100.0f).Delta(1.0f)
			.Value(Binding->Amount)
			.OnValueChanged_Lambda([this](float Value)
			{
				EnsureBinding().Amount = Value;
				GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
			}), 100.0f));
	}

	// What the key cap says; the action's own summary is the hint, and clearing the box goes back to it.
	AddRow(LOCTEXT("DisplayNameLabel", "Display name"), Field(
		SNew(SEditableTextBox)
		.Text(FText::FromString(Binding->DisplayName))
		.HintText(FText::Format(LOCTEXT("DisplayNameHint", "Default: {0}"), FText::FromString(MacroKeyboardDevice::CompactSummary(Binding->Describe()))))
		.SelectAllTextWhenFocused(true)
		.OnTextCommitted_Lambda([this](const FText& Text, ETextCommit::Type)
		{
			const FString Value = Text.ToString().TrimStartAndEnd();
			FMacroKeyboardBinding& Edited = EnsureBinding();
			if (Edited.DisplayName != Value)
			{
				Edited.DisplayName = Value;
				GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
				Device->Invalidate(EInvalidateWidgetReason::Paint);
			}
		})),
		LOCTEXT("DisplayNameTip", "Text shown on the key cap instead of the action summary (such as the shortcut). Leave empty for the default. Only affects this binding in this context."));

	// ── knob ─────────────────────────────────────────────────────────────────
	// Rotary controls report several steps per physical click; both accelerations belong to the binding, so each
	// editor context can choose its own feel.
	if (SelectedControl == TEXT("KNOB_CW") || SelectedControl == TEXT("KNOB_CCW"))
	{
		AddSection(LOCTEXT("SectionKnob", "Knob"));
		AddRow(LOCTEXT("DetentsLabel", "Detents per trigger"), Field(
			SNew(SSpinBox<int32>)
			.MinValue(1).MaxValue(20).Delta(1)
			.Value(Binding->DetentsPerTrigger)
			.OnValueChanged_Lambda([this](int32 Value)
			{
				EnsureBinding().DetentsPerTrigger = FMath::Max(1, Value);
				GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
			}), 100.0f),
			LOCTEXT("DetentsTip", "Each click of the knob reports several steps (2 on the W909): 2 means one trigger per click."));
		AddRow(LOCTEXT("HoldAccel", "Hold-to-turn multiplier"), Field(
			SNew(SSpinBox<int32>)
			.MinValue(1).MaxValue(100).Delta(1)
			.Value(Binding->HoldMultiplier)
			.OnValueChanged_Lambda([this](int32 Value)
			{
				EnsureBinding().HoldMultiplier = FMath::Clamp(Value, 1, 100);
				GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
			}), 100.0f),
			LOCTEXT("HoldAccelTip", "Turning while holding the knob down multiplies each step by this; releasing the knob goes straight back to normal. 1 = off.\nAbove 1, the knob press’s own binding in this context runs on release, and not at all if the knob was turned while held."));
		AddRow(FText::GetEmpty(),
			SNew(SCheckBox)
			.ToolTipText(LOCTEXT("KnobAccelTip", "Turning quickly moves further per step (×2 under 90 ms between steps, ×5 under 40 ms). Turn off when every click must be exactly one frame."))
			.IsChecked(Binding->bSpeedAcceleration ? ECheckBoxState::Checked : ECheckBoxState::Unchecked)
			.OnCheckStateChanged_Lambda([this](ECheckBoxState State)
			{
				EnsureBinding().bSpeedAcceleration = State == ECheckBoxState::Checked;
				GetMutableDefault<UMacroKeyboardEditorSettings>()->SaveConfig();
			})
			.Content()[SNew(STextBlock).Text(LOCTEXT("KnobAccel", "Speed acceleration"))]);
	}

	// ── trigger ──────────────────────────────────────────────────────────────
	AddSection(LOCTEXT("SectionTrigger", "Trigger"));
	AddRow(FText::GetEmpty(),
		SNew(SCheckBox)
		.IsChecked(Binding->bOnRelease ? ECheckBoxState::Checked : ECheckBoxState::Unchecked)
		.OnCheckStateChanged_Lambda([this](ECheckBoxState State)
		{
			EnsureBinding().bOnRelease = State == ECheckBoxState::Checked;
			SaveSettings();
		})
		.Content()[SNew(STextBlock).Text(LOCTEXT("OnRelease", "Trigger on release (default: on press)"))]);

	// The knob press itself: say when this context uses it as the hold-to-accelerate modifier.
	if (SelectedControl == TEXT("KNOB_PRESS"))
	{
		const UMacroKeyboardEditorSettings& Settings = UMacroKeyboardEditorSettings::Get();
		const FMacroKeyboardBinding* Cw = Settings.FindBinding(Context, TEXT("KNOB_CW"));
		const FMacroKeyboardBinding* Ccw = Settings.FindBinding(Context, TEXT("KNOB_CCW"));
		if ((Cw != nullptr && Cw->HoldMultiplier > 1) || (Ccw != nullptr && Ccw->HoldMultiplier > 1))
		{
			DetailsBox->AddSlot().AutoHeight().Padding(LabelWidth, 4, 0, 0)
			[
				Muted(LOCTEXT("KnobModifierNote", "In this context the knob press is the hold-to-accelerate modifier: its binding runs on release, and not at all if the knob was turned while held."))
			];
		}
	}

	AddRestoreDefaults();
}

void SMacroKeyboardPanel::AddRestoreDefaults()
{
	// Tucked away at the bottom right and confirmed first: it replaces every binding in every context.
	DetailsBox->AddSlot().AutoHeight().Padding(0, 18, 0, 0)[SNew(SSeparator).Thickness(1.0f)];
	DetailsBox->AddSlot().AutoHeight().Padding(0, 8, 0, 0).HAlign(HAlign_Right)
	[
		SNew(SButton)
		.ButtonStyle(FAppStyle::Get(), "SimpleButton")
		.ToolTipText(LOCTEXT("RestoreDefaultsTip", "Replace every binding in every context with the plugin’s defaults"))
		.OnClicked_Lambda([this]()
		{
			const EAppReturnType::Type Answer = FMessageDialog::Open(EAppMsgType::YesNo,
				LOCTEXT("RestoreDefaultsConfirm", "Replace every binding in every context with the plugin defaults? Your current bindings will be lost."));
			if (Answer == EAppReturnType::Yes)
			{
				UMacroKeyboardEditorSettings* Settings = GetMutableDefault<UMacroKeyboardEditorSettings>();
				Settings->Bindings = UMacroKeyboardEditorSettings::DefaultBindings();
				SaveSettings();
			}
			return FReply::Handled();
		})
		[
			SNew(STextBlock).Text(LOCTEXT("RestoreDefaults", "Restore default bindings…")).ColorAndOpacity(UiMuted)
		]
	];
}

void SMacroKeyboardPanel::HandleControlEvent(const FMacroControlEvent& Event)
{
	if (Event.IsDown())
	{
		PressedControl = Event.Control;
		PressedAtSeconds = FPlatformTime::Seconds();
		if (Device.IsValid())
		{
			Device->Invalidate(EInvalidateWidgetReason::Paint);
		}
	}
}

EActiveTimerReturnType SMacroKeyboardPanel::TickHighlight(double InCurrentTime, float InDeltaTime)
{
	if (!PressedControl.IsEmpty() && FPlatformTime::Seconds() - PressedAtSeconds > HighlightSeconds)
	{
		PressedControl.Reset();
		if (Device.IsValid())
		{
			Device->Invalidate(EInvalidateWidgetReason::Paint);
		}
	}
	if (const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
	{
		const FString& Live = Subsystem->GetReportedContext();
		if (!Live.IsEmpty() && Live != PanelContext)
		{
			LastLiveContext = Live;
		}
	}
	return EActiveTimerReturnType::Continue;
}

#undef LOCTEXT_NAMESPACE
