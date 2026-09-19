// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardSubsystem.h"

#include "Engine/Engine.h"
#include "MacroKeyboardClient.h"
#include "MacroKeyboardSettings.h"

void UMacroKeyboardSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);

	TickHandle = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateUObject(this, &UMacroKeyboardSubsystem::Tick));
	if (UMacroKeyboardSettings::Get().bEnabled)
	{
		StartClient();
	}
}

void UMacroKeyboardSubsystem::Deinitialize()
{
	FTSTicker::GetCoreTicker().RemoveTicker(TickHandle);
	StopClient();
	Super::Deinitialize();
}

UMacroKeyboardSubsystem* UMacroKeyboardSubsystem::Get()
{
	return GEngine != nullptr ? GEngine->GetEngineSubsystem<UMacroKeyboardSubsystem>() : nullptr;
}

void UMacroKeyboardSubsystem::StartClient()
{
	StopClient();
	const UMacroKeyboardSettings& Settings = UMacroKeyboardSettings::Get();
	Client = MakeUnique<FMacroKeyboardClient>(Settings.PipeName, Settings.AppId, Settings.ReconnectSeconds);
	Client->Launch();
	UE_LOG(LogMacroKeyboard, Log, TEXT("waiting for MacroHub on \\\\.\\pipe\\%s"), *Settings.PipeName);
}

void UMacroKeyboardSubsystem::StopClient()
{
	if (Client.IsValid())
	{
		Client->Shutdown();
		Client.Reset();
	}
	ReportedContext.Reset();
	ReportedDetail.Reset();
}

void UMacroKeyboardSubsystem::Restart()
{
	StopClient();
	if (UMacroKeyboardSettings::Get().bEnabled)
	{
		StartClient();
	}
}

bool UMacroKeyboardSubsystem::IsConnected() const
{
	return Client.IsValid() && Client->IsConnected();
}

EMacroHubConnection UMacroKeyboardSubsystem::GetConnectionState() const
{
	if (!Client.IsValid())
	{
		return EMacroHubConnection::Disabled;
	}
	return Client->IsConnected() ? EMacroHubConnection::Connected : EMacroHubConnection::Connecting;
}

bool UMacroKeyboardSubsystem::IsPadConnected() const
{
	return Client.IsValid() && Client->IsPadConnected();
}

FString UMacroKeyboardSubsystem::DescribeConnection() const
{
	if (!Client.IsValid())
	{
		return TEXT("disabled");
	}
	return Client->IsConnected() ? Client->DescribeHub() : TEXT("connecting…");
}

void UMacroKeyboardSubsystem::ReportContext(const FString& ContextName, const FString& Detail)
{
	if (!IsConnected() || (ContextName == ReportedContext && Detail == ReportedDetail))
	{
		return;
	}
	ReportedContext = ContextName;
	ReportedDetail = Detail;

	FString Message = FString::Printf(TEXT("{\"type\":\"context\",\"name\":\"%s\""), *ContextName.ReplaceCharWithEscapedChar());
	if (!Detail.IsEmpty())
	{
		Message += FString::Printf(TEXT(",\"detail\":\"%s\""), *Detail.ReplaceCharWithEscapedChar());
	}
	Message += TEXT("}");
	Client->Send(Message);
}

void UMacroKeyboardSubsystem::SetLighting(const FMacroLightingSpec& Spec)
{
	if (!IsConnected() || (SentLighting.IsSet() && SentLighting.GetValue() == Spec))
	{
		return;
	}
	SentLighting = Spec;
	Client->Send(FString::Printf(
		TEXT("{\"type\":\"lighting\",\"mode\":%d,\"brightness\":%d,\"speed\":%d,\"direction\":%d,\"color\":\"%s\"}"),
		Spec.Mode, Spec.Brightness, Spec.Speed, Spec.Direction, *Spec.ColorHex()));
}

void UMacroKeyboardSubsystem::ResetLighting()
{
	if (!IsConnected() || !SentLighting.IsSet())
	{
		return;
	}
	SentLighting.Reset();
	Client->Send(TEXT("{\"type\":\"lighting\",\"reset\":true}"));
}

bool UMacroKeyboardSubsystem::Tick(float DeltaTime)
{
	if (!Client.IsValid())
	{
		return true;
	}

	// A reconnected Hub knows nothing about us: report the context again on the next ReportContext call.
	const bool bConnected = Client->IsConnected();
	if (bConnected && !bWasConnected)
	{
		ReportedContext.Reset();
		ReportedDetail.Reset();
	}
	bWasConnected = bConnected;

	TArray<FMacroControlInfo> NewControls;
	bool bDeviceChanged = false;
	while (Client->DeviceUpdates.Dequeue(NewControls))
	{
		Controls = MoveTemp(NewControls);
		bDeviceChanged = true;
	}
	if (bDeviceChanged)
	{
		OnDeviceChanged.Broadcast();
	}

	FString Notice;
	while (Client->Notices.Dequeue(Notice))
	{
		UE_LOG(LogMacroKeyboard, Log, TEXT("%s"), *Notice);
	}

	const bool bLog = UMacroKeyboardSettings::Get().bLogEvents;
	FMacroControlEvent Event;
	while (Client->Events.Dequeue(Event))
	{
		if (bLog)
		{
			UE_LOG(LogMacroKeyboard, Log, TEXT("%s"), *Event.ToString());
		}
		OnControlEvent.Broadcast(Event);
		OnControlEventBP.Broadcast(Event);
	}
	return true;
}
