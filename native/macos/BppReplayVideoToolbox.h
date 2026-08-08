#ifndef BPP_REPLAY_VIDEO_TOOLBOX_H
#define BPP_REPLAY_VIDEO_TOOLBOX_H

#include <stdint.h>

#if defined(__cplusplus)
extern "C"
{
#endif

typedef struct BppVtNativeStats
{
    int submittedFrames;
    int appendedFrames;
    int acquireMisses;
    int notReadyDrops;
    int encodeErrors;
    int maxInFlight;
} BppVtNativeStats;

int BppVtHasUnityMetalInterface(void);
int BppVtCanMuxAudio(void);
void *BppVtGetRenderEventFunc(void);
int BppVtCreate(
    const char *outputPath,
    int width,
    int height,
    int fps,
    int bitrateBitsPerSecond,
    void **handle);
int BppVtGetSlotCount(void *handle);
int BppVtAcquireSlot(void *handle, int *slotIndex);
void *BppVtPrepareRenderEvent(
    void *handle,
    int slotIndex,
    int64_t firstFrameIndex,
    int frameCount);
void BppVtCommitRenderEvent(void *eventData);
void BppVtCancelRenderEvent(void *eventData);
void BppVtDiscardRenderEvent(void *eventData);
void BppVtReleaseSlot(void *handle, int slotIndex);
int BppVtFinish(void *handle, int timeoutMs);
void BppVtDestroy(void *handle);
int BppVtIsFailed(void *handle);
void BppVtGetStats(void *handle, BppVtNativeStats *stats);
int BppVtCopyError(void *handle, char *buffer, int capacity);

int BppVtMuxAudio(
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

#endif
