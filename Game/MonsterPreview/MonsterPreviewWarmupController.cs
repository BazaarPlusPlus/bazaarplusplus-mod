using System;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewWarmupController : MonoBehaviour
{
    private bool _started;

    private async void Start()
    {
        if (_started)
            return;

        _started = true;

        await Task.Yield();

        var staticDataReady = await WarmStaticDataAsync();

        BppLog.Info(
            "MonsterPreviewWarmupController",
            $"Warmup finished staticDataReady={staticDataReady}"
        );
    }

    private static Task<bool> WarmStaticDataAsync()
    {
        try
        {
            return Task.FromResult(Data.GetStatic() != null);
        }
        catch (Exception ex)
        {
            BppLog.Error("MonsterPreviewWarmupController", "Static data warmup failed", ex);
            return Task.FromResult(false);
        }
    }
}
