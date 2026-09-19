// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"
#include "MacroKeyboardTypes.h"
#include "Subsystems/EngineSubsystem.h"
#include "MacroKeyboardSubsystem.generated.h"

class FMacroKeyboardClient;

DECLARE_MULTICAST_DELEGATE_OneParam(FOnMacroControlEvent, const FMacroControlEvent&);
DECLARE_MULTICAST_DELEGATE(FOnMacroDeviceChanged);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnMacroControlEventBP, const FMacroControlEvent&, Event);

/**
 * Owns the MacroHub connection and delivers control events on the game thread.
 *
 * Lives in the editor and in game builds; the editor module adds context detection and editor actions on top.
 * Events arrive on a background thread and are drained once per frame, so handlers run at a predictable point.
 */
UCLASS()
class MACROKEYBOARDRUNTIME_API UMacroKeyboardSubsystem : public UEngineSubsystem
{
	GENERATED_BODY()

public:
	// UEngineSubsystem
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	/** Native listeners (editor module, C++ gameplay code). Called on the game thread. */
	FOnMacroControlEvent OnControlEvent;

	/** Raised when MacroHub describes the device (connect) or its configuration changes. */
	FOnMacroDeviceChanged OnDeviceChanged;

	/** Blueprint listeners. */
	UPROPERTY(BlueprintAssignable, Category = "MacroKeyboard")
	FOnMacroControlEventBP OnControlEventBP;

	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	bool IsConnected() const;

	/** Controls of the connected pad, in device layout order. Empty until MacroHub describes them. */
	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	const TArray<FMacroControlInfo>& GetControls() const { return Controls; }

	/** Context the editor last reported to MacroHub. */
	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	const FString& GetReportedContext() const { return ReportedContext; }

	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	EMacroHubConnection GetConnectionState() const;

	/** True when MacroHub is connected and reports the keyboard as plugged in. */
	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	bool IsPadConnected() const;

	/** Short description of the connected Hub, for status displays. */
	UFUNCTION(BlueprintPure, Category = "MacroKeyboard")
	FString DescribeConnection() const;

	/**
	 * Tell MacroHub what the user is looking at (shown in its web UI). Repeated identical values are ignored,
	 * so this is cheap to call every frame.
	 */
	UFUNCTION(BlueprintCallable, Category = "MacroKeyboard")
	void ReportContext(const FString& ContextName, const FString& Detail);

	/**
	 * Drive the pad's backlight while this application is connected. MacroHub writes it to the device straight
	 * away and leaves it until something else changes it.
	 */
	UFUNCTION(BlueprintCallable, Category = "MacroKeyboard|Lighting")
	void SetLighting(const FMacroLightingSpec& Spec);

	/** Hand the backlight back to MacroHub (its per-layer / default lighting). */
	UFUNCTION(BlueprintCallable, Category = "MacroKeyboard|Lighting")
	void ResetLighting();

	/** Applies changed settings (pipe name, enabled) by restarting the connection. */
	UFUNCTION(BlueprintCallable, Category = "MacroKeyboard")
	void Restart();

	static UMacroKeyboardSubsystem* Get();

private:
	void StartClient();
	void StopClient();
	bool Tick(float DeltaTime);

	/** Last lighting sent, so repeated calls with the same state cost nothing. */
	TOptional<FMacroLightingSpec> SentLighting;

	TUniquePtr<FMacroKeyboardClient> Client;
	FTSTicker::FDelegateHandle TickHandle;
	TArray<FMacroControlInfo> Controls;
	FString ReportedContext;
	FString ReportedDetail;
	bool bWasConnected = false;
};
