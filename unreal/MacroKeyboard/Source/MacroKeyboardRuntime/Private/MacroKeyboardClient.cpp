// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardClient.h"

#include "Dom/JsonObject.h"
#include "HAL/PlatformProcess.h"
#include "HAL/RunnableThread.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
	/** The protocol version this plugin speaks (see docs/protocol.md). */
	constexpr int32 MacroHubProtocolVersion = 2;

	FString JsonEscape(const FString& Value)
	{
		FString Escaped = Value;
		Escaped.ReplaceInline(TEXT("\\"), TEXT("\\\\"));
		Escaped.ReplaceInline(TEXT("\""), TEXT("\\\""));
		return Escaped;
	}
}

FMacroKeyboardClient::FMacroKeyboardClient(const FString& InPipeName, const FString& InAppId, float InReconnectSeconds)
	: PipeName(InPipeName)
	, AppId(InAppId)
	, ReconnectSeconds(FMath::Clamp(InReconnectSeconds, 0.5f, 30.0f))
{
}

FMacroKeyboardClient::~FMacroKeyboardClient()
{
	Shutdown();
}

void FMacroKeyboardClient::Launch()
{
	if (Thread == nullptr)
	{
		Thread = FRunnableThread::Create(this, TEXT("MacroKeyboardClient"), 0, TPri_BelowNormal);
	}
}

void FMacroKeyboardClient::Shutdown()
{
	bStopping = true;
	Pipe.Close(); // unblocks the pending read
	if (Thread != nullptr)
	{
		Thread->WaitForCompletion();
		delete Thread;
		Thread = nullptr;
	}
}

void FMacroKeyboardClient::Stop()
{
	bStopping = true;
	Pipe.Close();
}

FString FMacroKeyboardClient::DescribeHub() const
{
	FScopeLock Lock(&InfoSection);
	if (HubVersion.IsEmpty())
	{
		return TEXT("MacroHub");
	}
	return FString::Printf(TEXT("MacroHub %s (protocol %d%s%s)"), *HubVersion, HubProtocol,
		ProfileForward.IsEmpty() ? TEXT("") : TEXT(", forward="), *ProfileForward);
}

uint32 FMacroKeyboardClient::Run()
{
	float Delay = ReconnectSeconds;
	TArray<uint8> Buffer;
	FString Pending;

	while (!bStopping)
	{
		if (!Pipe.Connect(PipeName))
		{
			// MacroHub is not running (or was restarted): wait, then try again with a growing delay.
			for (float Waited = 0.0f; Waited < Delay && !bStopping; Waited += 0.1f)
			{
				FPlatformProcess::Sleep(0.1f);
			}
			Delay = FMath::Min(Delay * 1.5f, ReconnectSeconds * 4.0f);
			continue;
		}

		Delay = ReconnectSeconds;
		bConnected = true;
		Pending.Reset();
		LastSeq = 0;
		SendHello();

		while (!bStopping)
		{
			Buffer.Reset();
			if (!Pipe.ReadSome(Buffer))
			{
				break; // pipe closed (Hub stopped) or Close() was called
			}

			Pending += FString(FUTF8ToTCHAR(reinterpret_cast<const ANSICHAR*>(Buffer.GetData()), Buffer.Num()));
			int32 NewLine = INDEX_NONE;
			while (Pending.FindChar(TEXT('\n'), NewLine))
			{
				FString Line = Pending.Left(NewLine);
				Pending.RightChopInline(NewLine + 1, EAllowShrinking::No);
				Line.TrimEndInline();
				if (!Line.IsEmpty())
				{
					HandleLine(Line);
				}
			}
		}

		bConnected = false;
		Pipe.Close();
		if (!bStopping)
		{
			Notices.Enqueue(TEXT("disconnected from MacroHub, reconnecting"));
		}
	}

	bConnected = false;
	return 0;
}

bool FMacroKeyboardClient::SendHello()
{
	const FString Hello = FString::Printf(
		TEXT("{\"type\":\"hello\",\"role\":\"app\",\"protocol\":%d,\"app\":\"%s\",\"pid\":%u,\"mode\":\"%s\"}"),
		MacroHubProtocolVersion, *JsonEscape(AppId), FPlatformProcess::GetCurrentProcessId(),
		GIsEditor ? TEXT("editor") : TEXT("game"));
	return Pipe.WriteLine(Hello);
}

void FMacroKeyboardClient::HandleLine(const FString& Line)
{
	TSharedPtr<FJsonObject> Root;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Line);
	if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
	{
		Notices.Enqueue(FString::Printf(TEXT("ignored malformed message: %s"), *Line.Left(120)));
		return;
	}

	const FString Type = Root->GetStringField(TEXT("type"));
	if (Type == TEXT("control"))
	{
		FMacroControlEvent Event;
		Event.Control = Root->GetStringField(TEXT("control"));
		Event.Phase = Root->GetStringField(TEXT("phase")) == TEXT("up") ? EMacroControlPhase::Up : EMacroControlPhase::Down;
		Root->TryGetStringField(TEXT("kind"), Event.Kind);
		Root->TryGetStringField(TEXT("part"), Event.Part);
		Root->TryGetStringField(TEXT("source"), Event.Source);
		Event.Seq = static_cast<int64>(Root->GetNumberField(TEXT("seq")));
		Root->TryGetNumberField(TEXT("t"), Event.TimestampMs);

		if (LastSeq != 0 && Event.Seq > LastSeq + 1)
		{
			const int64 Missed = Event.Seq - LastSeq - 1;
			DroppedEvents += Missed;
			Notices.Enqueue(FString::Printf(TEXT("missed %lld event(s) — the game thread was too slow to read them"), Missed));
		}
		LastSeq = Event.Seq;
		Events.Enqueue(MoveTemp(Event));
		return;
	}

	if (Type == TEXT("hello"))
	{
		FScopeLock Lock(&InfoSection);
		Root->TryGetStringField(TEXT("version"), HubVersion);
		Root->TryGetNumberField(TEXT("protocol"), HubProtocol);
		return;
	}

	if (Type == TEXT("welcome") || Type == TEXT("profile"))
	{
		const TArray<TSharedPtr<FJsonValue>>* ControlValues = nullptr;
		if (Root->TryGetArrayField(TEXT("controls"), ControlValues) && ControlValues != nullptr)
		{
			TArray<FMacroControlInfo> Controls;
			Controls.Reserve(ControlValues->Num());
			for (const TSharedPtr<FJsonValue>& Value : *ControlValues)
			{
				const TSharedPtr<FJsonObject>* Object = nullptr;
				if (!Value.IsValid() || !Value->TryGetObject(Object) || Object == nullptr)
				{
					continue;
				}
				FMacroControlInfo Info;
				Info.Id = (*Object)->GetStringField(TEXT("id"));
				(*Object)->TryGetStringField(TEXT("label"), Info.Label);
				(*Object)->TryGetStringField(TEXT("kind"), Info.Kind);
				(*Object)->TryGetStringField(TEXT("part"), Info.Part);
				const TArray<TSharedPtr<FJsonValue>>* RectValues = nullptr;
				if ((*Object)->TryGetArrayField(TEXT("rect"), RectValues) && RectValues != nullptr && RectValues->Num() == 4)
				{
					Info.Rect = FVector4((*RectValues)[0]->AsNumber(), (*RectValues)[1]->AsNumber(),
						(*RectValues)[2]->AsNumber(), (*RectValues)[3]->AsNumber());
				}
				if (!Info.Id.IsEmpty())
				{
					Controls.Add(MoveTemp(Info));
				}
			}
			if (Controls.Num() > 0)
			{
				DeviceUpdates.Enqueue(MoveTemp(Controls));
			}
		}

		bool bPad = false;
		if (Root->TryGetBoolField(TEXT("padConnected"), bPad))
		{
			bPadConnected = bPad;
		}

		const TSharedPtr<FJsonObject>* Profile = nullptr;
		FString Forward;
		if (Root->TryGetObjectField(TEXT("profile"), Profile) && Profile != nullptr && Profile->IsValid())
		{
			(*Profile)->TryGetStringField(TEXT("forward"), Forward);
		}
		{
			FScopeLock Lock(&InfoSection);
			ProfileForward = Forward;
		}
		if (Type == TEXT("welcome"))
		{
			Notices.Enqueue(FString::Printf(TEXT("connected to %s"), *DescribeHub()));
			if (!Forward.IsEmpty() && Forward != TEXT("controls"))
			{
				Notices.Enqueue(FString::Printf(
					TEXT("MacroHub is set to forward=\"%s\" for this application; set it to \"controls\" in the MacroHub web UI ")
					TEXT("(Applications → Forward to the app client → Forward raw controls) so the plugin decides what each control does"), *Forward));
			}
		}
		return;
	}

	if (Type == TEXT("error"))
	{
		Notices.Enqueue(FString::Printf(TEXT("MacroHub error: %s"), *Root->GetStringField(TEXT("message"))));
	}
}
