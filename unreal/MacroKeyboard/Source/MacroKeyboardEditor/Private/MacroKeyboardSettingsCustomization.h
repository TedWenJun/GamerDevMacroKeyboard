// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "IDetailCustomization.h"

/**
 * Puts the visual binding panel at the top of Editor Preferences → Plugins → MacroKeyboard (Editor), so the
 * settings page is the panel rather than a bare list of struct rows. The raw arrays stay below it for anyone who
 * wants to edit them by hand.
 */
class FMacroKeyboardSettingsCustomization : public IDetailCustomization
{
public:
	static TSharedRef<IDetailCustomization> MakeInstance();

	virtual void CustomizeDetails(IDetailLayoutBuilder& DetailBuilder) override;
};
