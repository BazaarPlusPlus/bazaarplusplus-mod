#nullable enable
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Game.MusicNotes;

// Owns only preview instances. Native ShowAsync can start its pulse after Show returns,
// so apply the fixed alpha in LateUpdate, including after asynchronous asset completion.
internal sealed class NativeMusicNotePreviewLayer : IDisposable
{
    private readonly MusicNoteSpawnHintPresenter _presenter;
    private readonly Action _killPulse;
    private readonly Action<float> _setAlpha;
    private readonly float _alpha;
    private readonly List<MusicNoteSpawnPlacement> _shown = [];
    internal List<MusicNoteSpawnPlacement> Placements { get; } = [];

    internal NativeMusicNotePreviewLayer(SocketEffectVfxCatalog catalog, float alpha)
    {
        _presenter = new MusicNoteSpawnHintPresenter(catalog);
        _alpha = alpha;
        _killPulse = AccessTools.MethodDelegate<Action>(
            AccessTools.Method(typeof(MusicNoteSpawnHintPresenter), "KillPulseTween"),
            _presenter
        );
        _setAlpha = AccessTools.MethodDelegate<Action<float>>(
            AccessTools.Method(typeof(MusicNoteSpawnHintPresenter), "SetHintAlpha"),
            _presenter
        );
    }

    internal void Refresh()
    {
        // Compare explicitly: remain independent of upstream placement equality semantics.
        if (
            Placements.Count == _shown.Count
            && Placements
                .Where((p, i) => p.MusicNote != _shown[i].MusicNote || p.Parent != _shown[i].Parent)
                .Any() == false
        )
            return;
        _shown.Clear();
        _shown.AddRange(Placements);
        _presenter.Show(Placements);
    }

    internal void ApplyBrightness()
    {
        if (_shown.Count == 0)
            return;
        _killPulse();
        _setAlpha(_alpha);
    }

    internal void Clear()
    {
        if (_shown.Count != 0)
            _presenter.Clear();
        _shown.Clear();
        Placements.Clear();
    }

    public void Dispose() => _presenter.Dispose();
}
