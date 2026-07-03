#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesModule : IBppFeature
{
    private readonly VoiceLinesRepository _repository = new();

    public void Start()
    {
        VoiceLineDisplay.Reset();
        VoiceLineVoObserverBridge.Reset();
        _repository.BeginLoad();
    }

    public void Stop()
    {
        VoiceLineVoObserverBridge.Reset();
        VoiceLineDisplay.Reset();
    }
}
