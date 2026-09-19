// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardSettings.h"
#include "MacroKeyboardTypes.h"
#include "Modules/ModuleManager.h"

DEFINE_LOG_CATEGORY(LogMacroKeyboard);

UMacroKeyboardSettings::UMacroKeyboardSettings()
{
	CategoryName = TEXT("Plugins");
	SectionName = TEXT("MacroKeyboard");
}

const UMacroKeyboardSettings& UMacroKeyboardSettings::Get()
{
	const UMacroKeyboardSettings* Settings = GetDefault<UMacroKeyboardSettings>();
	check(Settings != nullptr);
	return *Settings;
}

IMPLEMENT_MODULE(FDefaultModuleImpl, MacroKeyboardRuntime);
