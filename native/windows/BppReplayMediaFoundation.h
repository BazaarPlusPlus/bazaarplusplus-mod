#pragma once

#include <stdint.h>

#if defined(__cplusplus)
extern "C"
{
#endif

typedef struct BppMfNativeStats
{
    int submittedFrames;
    int writtenFrames;
    int acquireMisses;
    int enqueueRejects;
    int encodeErrors;
    int maxInFlight;
} BppMfNativeStats;

__declspec(dllexport) int __cdecl BppMfHasUnityD3D11Interface(void);
__declspec(dllexport) int __cdecl BppMfCanMuxAudio(void);
__declspec(dllexport) void *__cdecl BppMfGetRenderEventFunc(void);
__declspec(dllexport) int __cdecl BppMfCreate(
    const char *outputPath,
    void *sourceTexture,
    int width,
    int height,
    int fps,
    int bitrateBitsPerSecond,
    void **handle);
__declspec(dllexport) int __cdecl BppMfGetSlotCount(void *handle);
__declspec(dllexport) int __cdecl BppMfAcquireSlot(void *handle, int *slotIndex);
__declspec(dllexport) void *__cdecl BppMfPrepareRenderEvent(
    void *handle,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount);
__declspec(dllexport) void __cdecl BppMfCommitRenderEvent(void *eventData);
__declspec(dllexport) void __cdecl BppMfCancelRenderEvent(void *eventData);
__declspec(dllexport) void __cdecl BppMfDiscardRenderEvent(void *eventData);
__declspec(dllexport) void __cdecl BppMfReleaseSlot(void *handle, int slotIndex);
__declspec(dllexport) int __cdecl BppMfFinish(void *handle, int timeoutMs);
__declspec(dllexport) void __cdecl BppMfDestroy(void *handle);
__declspec(dllexport) int __cdecl BppMfIsFailed(void *handle);
__declspec(dllexport) void __cdecl BppMfGetStats(void *handle, BppMfNativeStats *stats);
__declspec(dllexport) int __cdecl BppMfCopyError(void *handle, char *buffer, int capacity);
__declspec(dllexport) int __cdecl BppMfCopyEncoderName(
    void *handle,
    char *buffer,
    int capacity);

__declspec(dllexport) int __cdecl BppMfMuxAudio(
    const char *silentVideoPath,
    const char *const *wavPaths,
    int wavPathCount,
    const char *finalPath,
    int audioBitrateBitsPerSecond,
    int timeoutMs,
    char *errorBuffer,
    int errorCapacity);

#if defined(__cplusplus)
}
#endif
