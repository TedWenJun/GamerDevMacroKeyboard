// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#pragma once

#include "CoreMinimal.h"

/**
 * Minimal named-pipe client for MacroHub (newline-delimited UTF-8 JSON, both directions).
 *
 * FPlatformNamedPipe is not used because its ReadBytes() requires an exact byte count, while the protocol is
 * line-based. The handle is opened for overlapped I/O: a synchronous handle serializes operations, so a write from
 * the game thread would sit behind the blocking read until the Hub happened to send something. With overlapped I/O
 * the reader thread and the game thread proceed independently; Close() wakes both.
 */
class FMacroKeyboardPipe
{
public:
	~FMacroKeyboardPipe();

	/** Connects to \\.\pipe\<PipeName>. Returns false when MacroHub is not running. */
	bool Connect(const FString& PipeName);

	/** Blocks until data arrives; returns false when the pipe closed or Close() was called. */
	bool ReadSome(TArray<uint8>& OutBytes);

	/** Safe to call from another thread while a read is pending. */
	bool WriteLine(const FString& Line);

	void Close();
	bool IsConnected() const { return Handle != nullptr; }

private:
	void CloseHandles();

	void* Handle = nullptr;
	void* ReadEvent = nullptr;
	void* WriteEvent = nullptr;
	/** Signalled by Close() to abort a pending read. */
	void* CancelEvent = nullptr;
	mutable FCriticalSection WriteSection;
};
