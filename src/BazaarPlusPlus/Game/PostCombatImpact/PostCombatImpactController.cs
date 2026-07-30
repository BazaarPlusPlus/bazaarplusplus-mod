#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.GameInterop.Recap;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactController : MonoBehaviour
{
    private PostCombatImpactModule? _module;
    private PostCombatImpactView? _view;
    private readonly PostCombatImpactRecapState _recapState = new();

    internal void Initialize(PostCombatImpactModule module)
    {
        _module = module;
        _view = new PostCombatImpactView(transform, CloseThroughNativeBack);
        Events.RecapStarted.AddListener(OnRecapStarted, this);
        Events.RecapEnded.AddListener(OnRecapEnded, this);
    }

    private void OnRecapStarted()
    {
        _recapState.RecapStarted();
    }

    private void OnRecapEnded()
    {
        Apply(_recapState.RecapEnded());
    }

    private void Update()
    {
        var recapOpen = Singleton<BoardManager>.Instance?.IsRecapViewOpen == true;
        Apply(_recapState.Observe(recapOpen));
    }

    private void Apply(PostCombatImpactRecapTransition transition)
    {
        if (transition == PostCombatImpactRecapTransition.Show)
            _view?.Show(_module?.LatestReport ?? Data.CombatImpactReport.Empty);
        else if (transition == PostCombatImpactRecapTransition.Hide)
            _view?.Hide();
    }

    private void CloseThroughNativeBack()
    {
        if (NativeRecapControls.TryInvokeBack())
        {
            _view?.Hide();
            return;
        }

        BppLog.WarnEvent(
            PostCombatImpactLogEvents.CloseDegraded,
            PostCombatImpactLogEvents.ReasonCode.Bind(PostCombatImpactReasonCode.NativeBackRejected)
        );
    }

    private void OnDestroy()
    {
        Events.RecapStarted.RemoveListener(OnRecapStarted);
        Events.RecapEnded.RemoveListener(OnRecapEnded);
        _view?.Dispose();
        _view = null;
        _module = null;
    }
}
