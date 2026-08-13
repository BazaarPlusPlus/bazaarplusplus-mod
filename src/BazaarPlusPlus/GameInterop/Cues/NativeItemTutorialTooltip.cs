#nullable enable
using System.Reflection;
using TheBazaar;
using TheBazaar.SequenceFramework;
using TheBazaar.Utilities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.GameInterop.Cues;

internal enum NativeItemTutorialTooltipFailureReason
{
    AssetLoadFailed,
    TemplateShapeChanged,
    PositioningConfigurationFailed,
    SequenceAddRejected,
    PresentationException,
}

/// <summary>
/// Presents a caller-owned text-only copy of the game's native "Drag an item to sell it"
/// tutorial bubble.
/// The stock sequence chooses an arbitrary sellable item and continues the first-run tutorial, so
/// this adapter replaces those conditions with one explicit target and owns completion itself.
/// </summary>
internal sealed class NativeItemTutorialTooltip : IDisposable
{
    // 7_DragAnItemToSellIt_Merchant2Tutorial_NodeSequenceDataModel_SO
    private const string TutorialSequenceAssetGuid = "6552e521dacd0d540bccc0e7f6862851";
    private const string CloneName = "BPP_PackageMerchantTutorialTooltip";

    private static readonly MethodInfo? SetPositioningMethod = typeof(PositioningCondition)
        .GetProperty(
            nameof(PositioningCondition.Positioning),
            BindingFlags.Instance | BindingFlags.Public
        )
        ?.GetSetMethod(nonPublic: true);
    private static readonly MethodInfo? SetRepositioningBehaviorMethod =
        typeof(PositioningCondition)
            .GetProperty(
                nameof(PositioningCondition.RepositioningBehavior),
                BindingFlags.Instance | BindingFlags.Public
            )
            ?.GetSetMethod(nonPublic: true);

    private readonly Action<NativeItemTutorialTooltipFailureReason, Exception?> _reportFailure;
    private readonly Action _presentationStarted;
    private readonly List<NodeSequenceDataModel> _retiredModels = [];
    private AsyncOperationHandle<NodeSequenceDataModel> _templateHandle;
    private NodeSequenceDataModel? _template;
    private NodeSequenceDataModel? _activeModel;
    private Transform? _pendingTarget;
    private string? _pendingText;
    private bool _hasTemplateHandle;
    private bool _isLoading;
    private bool _disposed;

    internal NativeItemTutorialTooltip(
        Action<NativeItemTutorialTooltipFailureReason, Exception?> reportFailure,
        Action presentationStarted
    )
    {
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        _presentationStarted =
            presentationStarted ?? throw new ArgumentNullException(nameof(presentationStarted));
    }

    internal void ShowOrUpdate(Transform target, string text)
    {
        if (_disposed || target == null || string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            _pendingTarget = target;
            _pendingText = text;

            if (_activeModel != null && IsActive(_activeModel))
            {
                ApplyPresentation(_activeModel, target, text);
                return;
            }

            DestroyInactiveActiveModel();
            if (_template != null)
            {
                StartPendingPresentation();
                return;
            }

            if (!_isLoading)
                LoadTemplateAsync();
        }
        catch (Exception ex)
        {
            _reportFailure(NativeItemTutorialTooltipFailureReason.PresentationException, ex);
        }
    }

    internal void Hide()
    {
        _pendingTarget = null;
        _pendingText = null;
        if (_activeModel == null)
            return;

        Retire(_activeModel);
        _activeModel = null;
    }

    internal void Tick()
    {
        try
        {
            for (var index = _retiredModels.Count - 1; index >= 0; index--)
            {
                var model = _retiredModels[index];
                if (TryCompleteOrSkip(model) && !IsActive(model))
                {
                    _retiredModels.RemoveAt(index);
                    UnityEngine.Object.Destroy(model);
                }
            }

            DestroyInactiveActiveModel();
        }
        catch (Exception ex)
        {
            _reportFailure(NativeItemTutorialTooltipFailureReason.PresentationException, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Hide();
        _disposed = true;
        Tick();
        if (!_isLoading)
            ReleaseTemplateHandle();
        _template = null;
    }

    private async void LoadTemplateAsync()
    {
        _isLoading = true;
        try
        {
            _templateHandle = Addressables.LoadAssetAsync<NodeSequenceDataModel>(
                TutorialSequenceAssetGuid
            );
            _hasTemplateHandle = true;
            while (!_templateHandle.IsDone)
                await Task.Yield();

            if (_disposed)
                return;

            if (
                _templateHandle.Status != AsyncOperationStatus.Succeeded
                || _templateHandle.Result == null
            )
            {
                _reportFailure(NativeItemTutorialTooltipFailureReason.AssetLoadFailed, null);
                return;
            }

            _template = _templateHandle.Result;
            StartPendingPresentation();
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _reportFailure(NativeItemTutorialTooltipFailureReason.AssetLoadFailed, ex);
        }
        finally
        {
            _isLoading = false;
            if (_disposed)
                ReleaseTemplateHandle();
        }
    }

    private void StartPendingPresentation()
    {
        if (
            _disposed
            || _template == null
            || _pendingTarget == null
            || string.IsNullOrWhiteSpace(_pendingText)
        )
            return;

        var model = UnityEngine.Object.Instantiate(_template);
        model.name = CloneName;
        if (!TryConfigure(model, _pendingTarget, _pendingText))
        {
            UnityEngine.Object.Destroy(model);
            return;
        }

        if (!Data.SequenceProcessor.AddNodeSequenceDataModel(model))
        {
            UnityEngine.Object.Destroy(model);
            _reportFailure(NativeItemTutorialTooltipFailureReason.SequenceAddRejected, null);
            return;
        }

        _activeModel = model;
        _presentationStarted();
    }

    private bool TryConfigure(NodeSequenceDataModel model, Transform target, string text)
    {
        // Never allow a future edit to the stock first-run tutorial to continue its chain from
        // this independent reminder.
        if (
            model.NodeSequencesToActivateOnCompletion.Length != 0
            || model.NodeSequencesToCloseOnActivation.Length != 0
            || model.NodeSequencesToCloseOnCompletion.Length != 0
        )
        {
            _reportFailure(NativeItemTutorialTooltipFailureReason.TemplateShapeChanged, null);
            return false;
        }

        var textDetail = model.GetSequenceDetail<TextSequenceDetail>();
        if (textDetail?.Text == null || textDetail.Text.Length == 0)
        {
            _reportFailure(NativeItemTutorialTooltipFailureReason.TemplateShapeChanged, null);
            return false;
        }

        model.ConditionsRequiredToActivate.Clear();
        model.ConditionsThatCauseCompletion.Clear();
        model.Behaviours.Clear();
        model.SequenceDetails.RemoveAll(detail => detail is FMODSequenceDetail);
        // The stock tutorial includes an illustrative card image beneath its copy. Reusing that
        // detail here shows an unrelated cropped card and forces the bubble to occupy most of the
        // encounter-choice board. Keep the native frame, pointer, and typography, but make this
        // reminder text-only.
        model.SequenceDetails.RemoveAll(detail => detail is ImageSequenceDetail);
        model.SequenceDetails.Add(new KeepAddressableInMemorySequenceDetail());

        var positioning = new DynamicTransformPositioningCondition();
        if (!TryConfigurePositioning(positioning))
        {
            _reportFailure(
                NativeItemTutorialTooltipFailureReason.PositioningConfigurationFailed,
                null
            );
            return false;
        }

        positioning.SetScreenspace(true);
        positioning.SetDynamicPositioningObject(target);
        model.ConditionsRequiredToActivate.Add(positioning);
        textDetail.Text[0] = new LocalizableText(text);
        return true;
    }

    private static bool TryConfigurePositioning(DynamicTransformPositioningCondition positioning)
    {
        if (SetPositioningMethod == null || SetRepositioningBehaviorMethod == null)
            return false;

        SetPositioningMethod.Invoke(positioning, [PositioningCondition.PositioningType.Left]);
        SetRepositioningBehaviorMethod.Invoke(
            positioning,
            [PositioningCondition.RepositionBehavior.Nudge]
        );
        return true;
    }

    private static void ApplyPresentation(
        NodeSequenceDataModel model,
        Transform target,
        string text
    )
    {
        model
            .GetConditionRequiredToActivate<DynamicTransformPositioningCondition>()
            ?.SetDynamicPositioningObject(target);
        var textDetail = model.GetSequenceDetail<TextSequenceDetail>();
        if (textDetail?.Text is { Length: > 0 })
            textDetail.Text[0] = new LocalizableText(text);

        model
            .SpawnedNodeSequenceComponent?.GetComponentInChildren<BasePointerDialogController>(
                includeInactive: true
            )
            ?.TestShow(
                target,
                PositioningCondition.PositioningType.Left,
                duration: 0f,
                text: text,
                delay: 0f
            );
    }

    private void Retire(NodeSequenceDataModel model)
    {
        if (!_retiredModels.Contains(model))
            _retiredModels.Add(model);
        TryCompleteOrSkip(model);
    }

    private static bool TryCompleteOrSkip(NodeSequenceDataModel model)
    {
        if (!IsActive(model))
            return true;

        if (model.SpawnedNodeSequenceComponent != null)
        {
            if (model.SpawnedNodeSequenceComponent.IsActivated)
                model.SpawnedNodeSequenceComponent.NodeSequence.Completed();
            return false;
        }

        if (!model.IsSpawned)
            model.SkipActivationCondition.SetSkipActivation(value: true);
        return false;
    }

    private void DestroyInactiveActiveModel()
    {
        if (_activeModel == null || IsActive(_activeModel))
            return;

        UnityEngine.Object.Destroy(_activeModel);
        _activeModel = null;
    }

    private static bool IsActive(NodeSequenceDataModel model) =>
        Data.SequenceProcessor.IsNodeActive(model);

    private void ReleaseTemplateHandle()
    {
        if (!_hasTemplateHandle)
            return;

        Addressables.Release(_templateHandle);
        _hasTemplateHandle = false;
    }
}
