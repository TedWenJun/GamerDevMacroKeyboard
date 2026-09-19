// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "Containers/Ticker.h"
#include "HAL/IConsoleManager.h"
#include "Editor.h"
#include "Framework/Application/SlateApplication.h"
#include "Styling/AppStyle.h"
#include "Framework/Docking/TabManager.h"
#include "ISequencer.h"
#include "ISequencerModule.h"
#include "MacroKeyboardActionRunner.h"
#include "MacroKeyboardEditorSettings.h"
#include "MacroKeyboardSubsystem.h"
#include "MacroKeyboardTypes.h"
#include "Modules/ModuleManager.h"
#include "MacroKeyboardSettingsCustomization.h"
#include "PropertyEditorModule.h"
#include "SMacroKeyboardPanel.h"
#include "ToolMenus.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/SOverlay.h"
#include "Widgets/Text/STextBlock.h"
#include "Widgets/Docking/SDockTab.h"
#include "WorkspaceMenuStructure.h"
#include "WorkspaceMenuStructureModule.h"

/**
 * Editor side of the plugin: works out what the user is looking at (Sequencer, animation editor, level editor, PIE),
 * looks up the binding for that context and runs it. Timeline actions arrive with milestone 3.
 */
class FMacroKeyboardEditorModule : public IModuleInterface
{
public:
	virtual void StartupModule() override
	{
		if (ISequencerModule* SequencerModule = FModuleManager::Get().LoadModulePtr<ISequencerModule>(TEXT("Sequencer")))
		{
			SequencerCreatedHandle = SequencerModule->RegisterOnSequencerCreated(
				FOnSequencerCreated::FDelegate::CreateRaw(this, &FMacroKeyboardEditorModule::HandleSequencerCreated));
		}

		// Editor Preferences > Plugins > MacroKeyboard (Editor) shows the same panel instead of raw struct rows.
		FPropertyEditorModule& PropertyModule = FModuleManager::LoadModuleChecked<FPropertyEditorModule>("PropertyEditor");
		PropertyModule.RegisterCustomClassLayout(UMacroKeyboardEditorSettings::StaticClass()->GetFName(),
			FOnGetDetailCustomizationInstance::CreateStatic(&FMacroKeyboardSettingsCustomization::MakeInstance));
		PropertyModule.NotifyCustomizationModuleChanged();

		// Window > MacroKeyboard: the visual binding editor.
		FGlobalTabmanager::Get()->RegisterNomadTabSpawner(SMacroKeyboardPanel::TabId,
			FOnSpawnTab::CreateRaw(this, &FMacroKeyboardEditorModule::SpawnPanelTab))
			.SetDisplayName(NSLOCTEXT("MacroKeyboard", "PanelTitle", "MacroKeyboard"))
			.SetTooltipText(NSLOCTEXT("MacroKeyboard", "PanelTooltip", "Macro pad: set what each control does per editor context"))
			.SetGroup(WorkspaceMenu::GetMenuStructure().GetToolsCategory())
			.SetIcon(FSlateIcon(FAppStyle::GetAppStyleSetName(), "SystemWideCommands.OpenKeyboardShortcuts"));

		ConsoleCommands.Add(IConsoleManager::Get().RegisterConsoleCommand(TEXT("MacroKeyboard.OpenPanel"),
			TEXT("Open the MacroKeyboard binding panel. An optional control id (e.g. KNOB_CW) is selected in it."),
			FConsoleCommandWithArgsDelegate::CreateLambda([](const TArray<FString>& Args)
			{
				if (Args.Num() > 0)
				{
					SMacroKeyboardPanel::SelectControl(Args[0]);
				}
				FGlobalTabmanager::Get()->TryInvokeTab(SMacroKeyboardPanel::TabId);
			}),
			ECVF_Default));

		ConsoleCommands.Add(IConsoleManager::Get().RegisterConsoleCommand(TEXT("MacroKeyboard.Status"),
			TEXT("Print the MacroHub connection, the detected editor context and the number of bindings."),
			FConsoleCommandDelegate::CreateRaw(this, &FMacroKeyboardEditorModule::PrintStatus),
			ECVF_Default));

		// Bottom status bar: one click to the panel, with a dot that says whether the keyboard is reachable.
		UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FMacroKeyboardEditorModule::RegisterStatusBar));

		// The subsystem may not exist yet while modules are still loading; bind on the first tick.
		TickHandle = FTSTicker::GetCoreTicker().AddTicker(
			FTickerDelegate::CreateRaw(this, &FMacroKeyboardEditorModule::Tick), 0.0f);
	}

	virtual void ShutdownModule() override
	{
		UToolMenus::UnRegisterStartupCallback(this);
		UToolMenus::UnregisterOwner(this);
		if (FPropertyEditorModule* PropertyModule = FModuleManager::GetModulePtr<FPropertyEditorModule>("PropertyEditor"))
		{
			PropertyModule->UnregisterCustomClassLayout(UMacroKeyboardEditorSettings::StaticClass()->GetFName());
		}
		if (FGlobalTabmanager::Get()->HasTabSpawner(SMacroKeyboardPanel::TabId))
		{
			FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(SMacroKeyboardPanel::TabId);
		}
		for (IConsoleCommand* Command : ConsoleCommands)
		{
			IConsoleManager::Get().UnregisterConsoleObject(Command);
		}
		ConsoleCommands.Reset();
		FTSTicker::GetCoreTicker().RemoveTicker(TickHandle);
		if (UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get())
		{
			Subsystem->OnControlEvent.Remove(EventHandle);
		}
		if (ISequencerModule* SequencerModule = FModuleManager::Get().GetModulePtr<ISequencerModule>(TEXT("Sequencer")))
		{
			SequencerModule->UnregisterOnSequencerCreated(SequencerCreatedHandle);
		}
	}

private:
	void RegisterStatusBar()
	{
		FToolMenuOwnerScoped Owner(this);
		UToolMenu* Menu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.StatusBar.ToolBar"));
		FToolMenuSection& Section = Menu->AddSection(TEXT("MacroKeyboard"), FText::GetEmpty(),
			FToolMenuInsert(NAME_None, EToolMenuInsertType::Last));
		Section.AddEntry(FToolMenuEntry::InitWidget(TEXT("MacroKeyboardStatus"), MakeStatusBarWidget(), FText::GetEmpty(), true, false));
	}

	/** Green: keyboard online. Amber: MacroHub up but no keyboard. Grey: MacroHub not running. */
	static FSlateColor StatusColor()
	{
		const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
		if (Subsystem == nullptr || Subsystem->GetConnectionState() != EMacroHubConnection::Connected)
		{
			return FSlateColor(FLinearColor(0.35f, 0.35f, 0.35f));
		}
		return Subsystem->IsPadConnected() ? FSlateColor(FLinearColor(0.1f, 0.8f, 0.3f)) : FSlateColor(FLinearColor(1.0f, 0.6f, 0.1f));
	}

	static FText StatusTooltip()
	{
		const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
		if (Subsystem == nullptr || Subsystem->GetConnectionState() != EMacroHubConnection::Connected)
		{
			return NSLOCTEXT("MacroKeyboard", "StatusOff", "Macro pad: MacroHub not connected (make sure MacroHub is running)\nClick to open the setup panel");
		}
		return FText::Format(Subsystem->IsPadConnected()
				? NSLOCTEXT("MacroKeyboard", "StatusOn", "Macro pad: connected\n{0}\nClick to open the setup panel")
				: NSLOCTEXT("MacroKeyboard", "StatusNoPad", "Macro pad: MacroHub is connected but no pad was found\n{0}\nClick to open the setup panel"),
			FText::FromString(Subsystem->DescribeConnection()));
	}

	static TSharedRef<SWidget> MakeStatusBarWidget()
	{
		return SNew(SButton)
			.ButtonStyle(&FAppStyle::Get().GetWidgetStyle<FButtonStyle>("StatusBar.StatusBarButton"))
			.ToolTipText_Static(&FMacroKeyboardEditorModule::StatusTooltip)
			.OnClicked_Lambda([]()
			{
				FGlobalTabmanager::Get()->TryInvokeTab(SMacroKeyboardPanel::TabId);
				return FReply::Handled();
			})
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
				[
					SNew(SOverlay)
					+ SOverlay::Slot()
					[
						SNew(SImage)
						.Image(FAppStyle::GetBrush("SystemWideCommands.OpenKeyboardShortcuts"))
						.ColorAndOpacity(FSlateColor::UseForeground())
					]
					+ SOverlay::Slot().HAlign(HAlign_Right).VAlign(VAlign_Bottom).Padding(0, 0, -3, -3)
					[
						SNew(SImage)
						.Image(FAppStyle::GetBrush("Icons.FilledCircle"))
						.DesiredSizeOverride(FVector2D(8.0f, 8.0f))
						.ColorAndOpacity_Static(&FMacroKeyboardEditorModule::StatusColor)
					]
				]
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(6, 0, 0, 0)
				[
					SNew(STextBlock)
					.TextStyle(&FAppStyle::Get().GetWidgetStyle<FTextBlockStyle>("NormalText"))
					.Text(NSLOCTEXT("MacroKeyboard", "StatusLabel", "Macro pad"))
				]
			];
	}

	TSharedRef<SDockTab> SpawnPanelTab(const FSpawnTabArgs& Args)
	{
		return SNew(SDockTab)
			.TabRole(ETabRole::NomadTab)
			[
				SNew(SMacroKeyboardPanel)
			];
	}

	void PrintStatus() const
	{
		const UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
		UE_LOG(LogMacroKeyboard, Display, TEXT("MacroHub: %s | context: %s (%s) | controls: %d | bindings: %d"),
			Subsystem != nullptr ? *Subsystem->DescribeConnection() : TEXT("no subsystem"),
			*CurrentContext, *CurrentDetail,
			Subsystem != nullptr ? Subsystem->GetControls().Num() : 0,
			UMacroKeyboardEditorSettings::Get().Bindings.Num());
	}

	void HandleSequencerCreated(TSharedRef<ISequencer> Sequencer)
	{
		Sequencers.Add(Sequencer.ToWeakPtr());
		Sequencers.RemoveAll([](const TWeakPtr<ISequencer>& Weak) { return !Weak.IsValid(); });
	}

	/** The Sequencer the user is working in, if any. */
	TSharedPtr<ISequencer> ActiveSequencer() const
	{
		for (int32 Index = Sequencers.Num() - 1; Index >= 0; --Index)
		{
			if (TSharedPtr<ISequencer> Sequencer = Sequencers[Index].Pin())
			{
				return Sequencer;
			}
		}
		return nullptr;
	}

	/** Context id used to look up bindings; Detail is shown in MacroHub's UI. */
	FString DetectContext(FString& OutDetail) const
	{
		OutDetail.Reset();
		if (GEditor != nullptr && GEditor->PlayWorld != nullptr)
		{
			return GEditor->bIsSimulatingInEditor ? TEXT("simulate") : TEXT("pie");
		}

		FString TabId;
		if (TSharedPtr<SDockTab> ActiveTab = FGlobalTabmanager::Get()->GetActiveTab())
		{
			TabId = ActiveTab->GetLayoutIdentifier().ToString();
			OutDetail = ActiveTab->GetTabLabel().ToString();
		}

		if (TabId.IsEmpty())
		{
			return TEXT("editor");
		}
		if (TabId.Contains(TEXT("Sequencer")) || TabId.Contains(TEXT("LevelSequence")))
		{
			return TEXT("sequencer");
		}
		if (TabId.Contains(TEXT("AnimationBlueprint")) || TabId.Contains(TEXT("AnimBlueprint")))
		{
			return TEXT("animBlueprint");
		}
		if (TabId.Contains(TEXT("Animation")) || TabId.Contains(TEXT("Persona")) || TabId.Contains(TEXT("SkeletalMesh")))
		{
			return TEXT("animation");
		}
		if (TabId.Contains(TEXT("LevelEditor")) || TabId.Contains(TEXT("Viewport")))
		{
			return TEXT("levelEditor");
		}
		// Unknown tab: keep the identifier so the log tells us what to map next.
		return FString::Printf(TEXT("editor:%s"), *TabId);
	}

	void HandleControlEvent(const FMacroControlEvent& Event)
	{
		const UMacroKeyboardEditorSettings& Settings = UMacroKeyboardEditorSettings::Get();

		// Hold-to-accelerate turns the knob press into a modifier. Its own binding then runs as a click on release,
		// and only when the knob was not turned while it was held. (MacroHub keeps the press down through the pad's
		// turn reports, which clear it in the firmware, so the press state here matches the finger.)
		if (Event.Control == TEXT("KNOB_PRESS") && KnobPressIsModifier(Settings))
		{
			if (Event.IsDown())
			{
				bKnobHeld = true;
				bKnobTurnedWhileHeld = false;
				return;
			}
			const bool bWasTurned = bKnobTurnedWhileHeld;
			bKnobHeld = false;
			bKnobTurnedWhileHeld = false;
			if (!bWasTurned)
			{
				RunBinding(Settings, Event, false, true);
			}
			return;
		}
		if (bKnobHeld && Event.IsRotary())
		{
			bKnobTurnedWhileHeld = true;
		}
		RunBinding(Settings, Event, bKnobHeld, false);
	}

	void RunBinding(const UMacroKeyboardEditorSettings& Settings, const FMacroControlEvent& Event, bool bKnobHeldNow, bool bDeferredKnobClick)
	{
		const FMacroKeyboardBinding* Binding = Settings.FindBinding(CurrentContext, Event.Control);
		if (Binding == nullptr)
		{
			UE_LOG(LogMacroKeyboard, Verbose, TEXT("%s: no binding in context '%s'"), *Event.Control, *CurrentContext);
			return;
		}
		if (!bDeferredKnobClick && Event.IsDown() == Binding->bOnRelease)
		{
			return; // this binding runs on the other phase
		}

		// The pad reports several steps per physical knob click; the binding says how many make one trigger.
		// Count on the phase the binding fires on (press or release), otherwise "on release" skips the gate entirely.
		if (Binding->DetentsPerTrigger > 1)
		{
			// Turning back the other way starts a new click: a half-counted step left on the other direction must
			// not fire early when the user reverses again.
			for (TPair<FString, int32>& Other : DetentCounts)
			{
				if (Other.Key != Event.Control)
				{
					Other.Value = 0;
				}
			}

			const double Now = FPlatformTime::Seconds();
			double& Last = LastDetentSeconds.FindOrAdd(Event.Control);
			int32& Count = DetentCounts.FindOrAdd(Event.Control);
			if (Now - Last > 0.8)
			{
				Count = 0; // a pause means the user let go: start the next click from scratch
			}
			Last = Now;
			if (++Count < Binding->DetentsPerTrigger)
			{
				return;
			}
			Count = 0;
		}

		FMacroKeyboardContext Context;
		Context.Name = CurrentContext;
		Context.Detail = CurrentDetail;
		Context.bKnobAcceleration = Binding->bSpeedAcceleration;
		Context.HoldMultiplier = bKnobHeldNow ? FMath::Max(1, Binding->HoldMultiplier) : 1;
		if (CurrentContext == TEXT("sequencer"))
		{
			Context.Sequencer = ActiveSequencer();
		}

		const bool bDone = FMacroKeyboardActionRunner::Execute(*Binding, Event, Context);
		// One line per press is noise unless the user asked for it (Settings > Diagnostics); Verbose keeps it reachable.
		if (Settings.bLogControlEvents)
		{
			UE_LOG(LogMacroKeyboard, Log, TEXT("%s [%s] -> %s%s"), *Event.Control, *CurrentContext, *Binding->Describe(),
				bDone ? TEXT("") : TEXT(" (not handled)"));
		}
		else
		{
			UE_LOG(LogMacroKeyboard, Verbose, TEXT("%s [%s] -> %s%s"), *Event.Control, *CurrentContext, *Binding->Describe(),
				bDone ? TEXT("") : TEXT(" (not handled)"));
		}
	}

	bool Tick(float DeltaTime)
	{
		UMacroKeyboardSubsystem* Subsystem = UMacroKeyboardSubsystem::Get();
		if (Subsystem == nullptr)
		{
			return true;
		}
		if (!EventHandle.IsValid())
		{
			EventHandle = Subsystem->OnControlEvent.AddRaw(this, &FMacroKeyboardEditorModule::HandleControlEvent);
		}

		FString Detail;
		const FString Context = DetectContext(Detail);
		if (Context != CurrentContext)
		{
			UE_LOG(LogMacroKeyboard, Verbose, TEXT("context: %s (%s)"), *Context, *Detail);
			CurrentContext = Context;
			ApplyLighting(*Subsystem, Context);
		}
		CurrentDetail = Detail;
		Subsystem->ReportContext(Context, Detail);
		return true;
	}

	/** Tint the pad for the context that just came to the front, or hand the backlight back to MacroHub. */
	void ApplyLighting(UMacroKeyboardSubsystem& Subsystem, const FString& Context)
	{
		const UMacroKeyboardEditorSettings& Settings = UMacroKeyboardEditorSettings::Get();
		const FMacroContextLighting* Entry = Settings.bDriveLighting ? Settings.FindLighting(Context) : nullptr;
		if (Entry != nullptr)
		{
			Subsystem.SetLighting(Entry->Lighting);
		}
		else
		{
			Subsystem.ResetLighting();
		}
	}

	/** Detent accumulation per control, for bindings that want several steps per trigger. */
	/** In this context, is the knob press a modifier? Yes when either turn direction accelerates while held. */
	bool KnobPressIsModifier(const UMacroKeyboardEditorSettings& Settings) const
	{
		for (const TCHAR* Turn : { TEXT("KNOB_CW"), TEXT("KNOB_CCW") })
		{
			const FMacroKeyboardBinding* TurnBinding = Settings.FindBinding(CurrentContext, Turn);
			if (TurnBinding != nullptr && TurnBinding->HoldMultiplier > 1)
			{
				return true;
			}
		}
		return false;
	}

	/** Knob press state for hold-to-accelerate. */
	bool bKnobHeld = false;
	bool bKnobTurnedWhileHeld = false;

	TMap<FString, int32> DetentCounts;
	TMap<FString, double> LastDetentSeconds;

	TArray<IConsoleCommand*> ConsoleCommands;
	FTSTicker::FDelegateHandle TickHandle;
	FDelegateHandle EventHandle;
	FDelegateHandle SequencerCreatedHandle;
	TArray<TWeakPtr<ISequencer>> Sequencers;
	FString CurrentContext;
	FString CurrentDetail;
};

IMPLEMENT_MODULE(FMacroKeyboardEditorModule, MacroKeyboardEditor);
