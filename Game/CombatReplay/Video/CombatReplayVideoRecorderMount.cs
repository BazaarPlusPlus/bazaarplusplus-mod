#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class CombatReplayVideoRecorderMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var recorder = host.AddComponent<CombatReplayVideoRecorder>();
        recorder.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var recorder = host.GetComponent<CombatReplayVideoRecorder>();
        if (recorder != null)
            Object.DestroyImmediate(recorder);
    }
}
