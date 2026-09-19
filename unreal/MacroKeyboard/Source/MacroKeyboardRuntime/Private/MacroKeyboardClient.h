// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"
#include "Containers/Queue.h"
#include "HAL/Runnable.h"
#include "HAL/ThreadSafeBool.h"
#include "MacroKeyboardPipe.h"
#include "MacroKeyboardTypes.h"

class FRunnableThread;

/**
 * Background thread that keeps the MacroHub connection alive and turns incoming lines into events.
 *
 * Only this thread touches the pipe reader; the game thread drains the lock-free queues once per frame and may
 * call Send() (guarded inside the pipe). Reconnects with a growing delay while MacroHub is not running.
 */
class FMacroKeyboardClient : public FRunnable
{
public:
	FMacroKeyboardClient(const FString& InPipeName, const FString& InAppId, float InReconnectSeconds);
	virtual ~FMacroKeyboardClient() override;

	void Launch();
	void Shutdown();

	/** Control events, game thread consumes. */
	TQueue<FMacroControlEvent, EQueueMode::Spsc> Events;
	/** Human readable notices (hello/welcome/errors) to log on the game thread. */
	TQueue<FString, EQueueMode::Spsc> Notices;
	/** Device description from the last welcome/profile message; the game thread swaps it in. */
	TQueue<TArray<FMacroControlInfo>, EQueueMode::Spsc> DeviceUpdates;

	bool IsConnected() const { return bConnected; }
	/** Whether MacroHub reports the keyboard itself as plugged in (from welcome/profile). */
	bool IsPadConnected() const { return bConnected && bPadConnected; }
	bool Send(const FString& JsonLine) { return Pipe.WriteLine(JsonLine); }

	/** Number of events MacroHub dropped because we read too slowly (detected through seq gaps). */
	int64 GetDroppedEvents() const { return DroppedEvents; }

	FString DescribeHub() const;

	// FRunnable
	virtual uint32 Run() override;
	virtual void Stop() override;

private:
	bool SendHello();
	void HandleLine(const FString& Line);

	FMacroKeyboardPipe Pipe;
	FRunnableThread* Thread = nullptr;
	FThreadSafeBool bStopping{ false };
	FThreadSafeBool bConnected{ false };
	FThreadSafeBool bPadConnected{ false };

	const FString PipeName;
	const FString AppId;
	const float ReconnectSeconds;

	int64 LastSeq = 0;
	int64 DroppedEvents = 0;

	mutable FCriticalSection InfoSection;
	FString HubVersion;
	int32 HubProtocol = 0;
	FString ProfileForward;
};
