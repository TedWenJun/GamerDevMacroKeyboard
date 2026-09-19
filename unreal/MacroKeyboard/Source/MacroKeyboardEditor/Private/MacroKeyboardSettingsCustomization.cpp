// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardSettingsCustomization.h"

#include "DetailCategoryBuilder.h"
#include "DetailLayoutBuilder.h"
#include "DetailWidgetRow.h"
#include "Framework/Docking/TabManager.h"
#include "SMacroKeyboardPanel.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "MacroKeyboard"

TSharedRef<IDetailCustomization> FMacroKeyboardSettingsCustomization::MakeInstance()
{
	return MakeShared<FMacroKeyboardSettingsCustomization>();
}

void FMacroKeyboardSettingsCustomization::CustomizeDetails(IDetailLayoutBuilder& DetailBuilder)
{
	IDetailCategoryBuilder& Category = DetailBuilder.EditCategory(
		TEXT("Panel"), LOCTEXT("PanelCategory", "Setup panel"), ECategoryPriority::Important);

	Category.AddCustomRow(LOCTEXT("PanelRowFilter", "setup panel keyboard lighting"))
		.WholeRowContent()
		[
			SNew(SVerticalBox)

			+ SVerticalBox::Slot().AutoHeight().Padding(0, 0, 0, 6)
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().FillWidth(1.0f).VAlign(VAlign_Center)
				[
					SNew(STextBlock)
					.Text(LOCTEXT("PanelHint", "Click a key on the pad to edit its binding; the backlight settings are on the right. The panel also opens as its own window from Window > Tools > MacroKeyboard."))
					.AutoWrapText(true)
					.ColorAndOpacity(FSlateColor::UseSubduedForeground())
				]
			]

			+ SVerticalBox::Slot().AutoHeight()
			[
				// The panel lays itself out top-down; give it room so the device drawing is not squeezed.
				SNew(SBox).MinDesiredHeight(520.0f)
				[
					SNew(SMacroKeyboardPanel).ShowOpenInWindow(true)
				]
			]
		];
}

#undef LOCTEXT_NAMESPACE
