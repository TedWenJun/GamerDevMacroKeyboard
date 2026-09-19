// Copyright (c) 2026 MacroHub contributors. MIT licensed.

#include "MacroKeyboardPipe.h"

#include "MacroKeyboardTypes.h"

#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include <windows.h>
#include "Windows/HideWindowsPlatformTypes.h"
#endif

FMacroKeyboardPipe::~FMacroKeyboardPipe()
{
	Close();
}

bool FMacroKeyboardPipe::Connect(const FString& PipeName)
{
#if PLATFORM_WINDOWS
	Close();
	const FString FullName = FString::Printf(TEXT("\\\\.\\pipe\\%s"), *PipeName);
	HANDLE Pipe = ::CreateFileW(*FullName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
	if (Pipe == INVALID_HANDLE_VALUE)
	{
		const uint32 Error = ::GetLastError();
		// ERROR_FILE_NOT_FOUND simply means MacroHub is not running; anything else is worth a log line.
		if (Error != ERROR_FILE_NOT_FOUND)
		{
			UE_LOG(LogMacroKeyboard, Verbose, TEXT("connect to %s failed (error %u)"), *FullName, Error);
		}
		return false;
	}

	FScopeLock Lock(&WriteSection);
	Handle = Pipe;
	ReadEvent = ::CreateEventW(nullptr, true, false, nullptr);
	WriteEvent = ::CreateEventW(nullptr, true, false, nullptr);
	CancelEvent = ::CreateEventW(nullptr, true, false, nullptr);
	return true;
#else
	return false;
#endif
}

bool FMacroKeyboardPipe::ReadSome(TArray<uint8>& OutBytes)
{
#if PLATFORM_WINDOWS
	if (Handle == nullptr)
	{
		return false;
	}

	uint8 Buffer[4096];
	OVERLAPPED Overlapped = {};
	Overlapped.hEvent = static_cast<HANDLE>(ReadEvent);
	::ResetEvent(static_cast<HANDLE>(ReadEvent));

	DWORD Read = 0;
	if (!::ReadFile(static_cast<HANDLE>(Handle), Buffer, sizeof(Buffer), &Read, &Overlapped))
	{
		if (::GetLastError() != ERROR_IO_PENDING)
		{
			return false; // pipe closed
		}
		HANDLE WaitOn[2] = { static_cast<HANDLE>(ReadEvent), static_cast<HANDLE>(CancelEvent) };
		const DWORD Wait = ::WaitForMultipleObjects(2, WaitOn, false, INFINITE);
		if (Wait != WAIT_OBJECT_0)
		{
			::CancelIoEx(static_cast<HANDLE>(Handle), &Overlapped);
			return false; // Close() was called
		}
		if (!::GetOverlappedResult(static_cast<HANDLE>(Handle), &Overlapped, &Read, false))
		{
			return false;
		}
	}
	if (Read == 0)
	{
		return false;
	}
	OutBytes.Append(Buffer, static_cast<int32>(Read));
	return true;
#else
	return false;
#endif
}

bool FMacroKeyboardPipe::WriteLine(const FString& Line)
{
#if PLATFORM_WINDOWS
	FScopeLock Lock(&WriteSection);
	if (Handle == nullptr)
	{
		return false;
	}

	const FTCHARToUTF8 Utf8(*(Line + TEXT("\n")));
	OVERLAPPED Overlapped = {};
	Overlapped.hEvent = static_cast<HANDLE>(WriteEvent);
	::ResetEvent(static_cast<HANDLE>(WriteEvent));

	DWORD Written = 0;
	if (!::WriteFile(static_cast<HANDLE>(Handle), Utf8.Get(), static_cast<DWORD>(Utf8.Length()), &Written, &Overlapped))
	{
		if (::GetLastError() != ERROR_IO_PENDING)
		{
			return false;
		}
		// The Hub reads continuously, so this completes immediately; the timeout only guards against a stuck peer.
		if (::WaitForSingleObject(static_cast<HANDLE>(WriteEvent), 2000) != WAIT_OBJECT_0)
		{
			::CancelIoEx(static_cast<HANDLE>(Handle), &Overlapped);
			UE_LOG(LogMacroKeyboard, Warning, TEXT("timed out sending to MacroHub"));
			return false;
		}
		if (!::GetOverlappedResult(static_cast<HANDLE>(Handle), &Overlapped, &Written, false))
		{
			return false;
		}
	}
	return Written == static_cast<DWORD>(Utf8.Length());
#else
	return false;
#endif
}

void FMacroKeyboardPipe::Close()
{
#if PLATFORM_WINDOWS
	if (CancelEvent != nullptr)
	{
		::SetEvent(static_cast<HANDLE>(CancelEvent)); // wake a pending read before taking the lock
	}
	FScopeLock Lock(&WriteSection);
	CloseHandles();
#endif
}

void FMacroKeyboardPipe::CloseHandles()
{
#if PLATFORM_WINDOWS
	if (Handle != nullptr)
	{
		::CancelIoEx(static_cast<HANDLE>(Handle), nullptr);
		::CloseHandle(static_cast<HANDLE>(Handle));
		Handle = nullptr;
	}
	for (void** Event : { &ReadEvent, &WriteEvent, &CancelEvent })
	{
		if (*Event != nullptr)
		{
			::CloseHandle(static_cast<HANDLE>(*Event));
			*Event = nullptr;
		}
	}
#endif
}
