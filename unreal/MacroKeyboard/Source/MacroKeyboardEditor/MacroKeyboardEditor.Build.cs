// Copyright (c) 2026 MacroHub contributors. MIT licensed.

using UnrealBuildTool;

public class MacroKeyboardEditor : ModuleRules
{
	public MacroKeyboardEditor(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core",
			"CoreUObject",
			"Engine",
			"DeveloperSettings",
			"MacroKeyboardRuntime",
		});

		PrivateDependencyModuleNames.AddRange(new string[]
		{
			"Slate",
			"SlateCore",
			"InputCore",
			"UnrealEd",
				"PropertyEditor",
				"ToolMenus",
				"AppFramework",   // SColorPicker
			"LevelEditor",
			"Sequencer",
			"Persona",
			"AnimGraph",
			"MovieScene",
			"WorkspaceMenuStructure",
		});
	}
}
