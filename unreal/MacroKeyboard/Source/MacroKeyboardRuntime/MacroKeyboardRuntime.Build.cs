// Copyright (c) 2026 MacroHub contributors. MIT licensed.

using UnrealBuildTool;

public class MacroKeyboardRuntime : ModuleRules
{
	public MacroKeyboardRuntime(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core",
			"CoreUObject",
			"Engine",
			"DeveloperSettings",
		});

		PrivateDependencyModuleNames.AddRange(new string[]
		{
			"Json",
		});
	}
}
