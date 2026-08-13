#nullable enable
using System.Collections;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Cues;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.PackageMerchantReminder;

internal enum PackageMerchantReminderAnchor
{
    VisiblePackage,
    ClosedStash,
}

internal enum PackageMerchantReminderFailureReason
{
    EventCallbackException,
    InventoryReadException,
    PresentationReadException,
    ReadyTimeout,
}

internal sealed class PackageMerchantReminderController : MonoBehaviour
{
    private const float ReminderVisibleSeconds = 4f;
    private const float PresentationReadyTimeoutSeconds = 10f;

    private readonly HashSet<Guid> _merchantCandidateTemplateIds = [];
    private readonly HashSet<string> _soldInstanceIds = new(StringComparer.Ordinal);
    private NativeItemTutorialTooltip? _tooltip;
    private Coroutine? _autoHideCoroutine;
    private Coroutine? _refreshCoroutine;
    private string? _currentPackageInstanceId;
    private Guid _merchantTemplateId;
    private PackageMerchantReminderAnchor _lastAnchor;
    private int _entryVersion;
    private bool _entryArmed;
    private bool _shownThisEntry;
    private bool _initialized;

    internal void Initialize()
    {
        if (_initialized)
            return;

        _tooltip = new NativeItemTutorialTooltip(ReportNativeFailure, OnPresentationStarted);
        Events.StateTransitionedSimEvent.AddListener(OnStateTransitioned, this);
        Events.CardDealtSimEvent.AddListener(OnCardsDealt, this);
        Events.CardDisposedSimEvent.AddListener(OnCardsDisposed, this);
        Events.StorageToggled.AddListener(OnStorageToggled, this);
        Events.CardSoldSimEvent.AddListener(OnCardSold, this);
        Events.OnCardMoved.AddListener(OnCardMoved, this);
        _initialized = true;
    }

    private void Update() => _tooltip?.Tick();

    private void OnDestroy()
    {
        if (!_initialized)
            return;

        Events.StateTransitionedSimEvent.RemoveListener(OnStateTransitioned);
        Events.CardDealtSimEvent.RemoveListener(OnCardsDealt);
        Events.CardDisposedSimEvent.RemoveListener(OnCardsDisposed);
        Events.StorageToggled.RemoveListener(OnStorageToggled);
        Events.CardSoldSimEvent.RemoveListener(OnCardSold);
        Events.OnCardMoved.RemoveListener(OnCardMoved);
        StopRefresh();
        StopAutoHide();
        _tooltip?.Dispose();
        _tooltip = null;
        _initialized = false;
    }

    private void OnStateTransitioned(GameSimEventStateTransitioned stateTransition)
    {
        try
        {
            _entryVersion++;
            _entryArmed = false;
            _shownThisEntry = false;
            _currentPackageInstanceId = null;
            _merchantTemplateId = Guid.Empty;
            _merchantCandidateTemplateIds.Clear();
            _soldInstanceIds.Clear();
            StopRefresh();
            StopAutoHide();
            _tooltip?.Hide();

            // Package reminders belong to the encounter-choice screen: the player needs the
            // reminder when the matching merchant is revealed, before choosing that merchant.
            // CardDealtSimEvent supplies the candidate templates after this state transition.
            if (stateTransition.ToState != ERunState.Choice)
                return;
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.EventHandler,
                PackageMerchantReminderFailureReason.EventCallbackException,
                ex
            );
            DisarmEntry();
        }
    }

    private void OnCardsDealt(List<Card> cards)
    {
        try
        {
            if (AppState.CurrentState is not ChoiceState)
                return;

            var addedCandidate = false;
            foreach (var card in cards)
            {
                if (card is not EventEncounterCard || card.TemplateId == Guid.Empty)
                    continue;

                addedCandidate |= _merchantCandidateTemplateIds.Add(card.TemplateId);
            }

            if (!addedCandidate)
                return;

            _entryVersion++;
            _entryArmed = true;
            _shownThisEntry = false;
            StartRefresh();
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.EventHandler,
                PackageMerchantReminderFailureReason.EventCallbackException,
                ex
            );
            DisarmEntry();
        }
    }

    private void OnCardsDisposed(List<Card> cards)
    {
        try
        {
            var removedCandidate = false;
            foreach (var card in cards)
            {
                if (card is EventEncounterCard)
                    removedCandidate |= _merchantCandidateTemplateIds.Remove(card.TemplateId);
            }

            if (!removedCandidate || AppState.CurrentState is not ChoiceState)
                return;

            _entryVersion++;
            _merchantTemplateId = Guid.Empty;
            _currentPackageInstanceId = null;
            _tooltip?.Hide();

            if (_merchantCandidateTemplateIds.Count == 0)
            {
                DisarmEntry();
                return;
            }

            _entryArmed = true;
            _shownThisEntry = false;
            StartRefresh();
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.EventHandler,
                PackageMerchantReminderFailureReason.EventCallbackException,
                ex
            );
            DisarmEntry();
        }
    }

    private void OnStorageToggled(bool isOpen)
    {
        if (isOpen && _entryArmed)
        {
            DisarmEntry();
            return;
        }

        TryRefreshAfterEvent();
    }

    private void OnCardSold(GameSimEventCardSold sold)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(sold.InstanceId))
                _soldInstanceIds.Add(sold.InstanceId);
            if (_entryArmed)
            {
                if (
                    string.Equals(
                        sold.InstanceId,
                        _currentPackageInstanceId,
                        StringComparison.Ordinal
                    )
                )
                {
                    _currentPackageInstanceId = null;
                    _tooltip?.Hide();
                }
                StartRefresh();
            }
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.EventHandler,
                PackageMerchantReminderFailureReason.EventCallbackException,
                ex
            );
            DisarmEntry();
        }
    }

    private void OnCardMoved(ItemCard _)
    {
        TryRefreshAfterEvent();
    }

    private void TryRefreshAfterEvent()
    {
        try
        {
            if (_entryArmed)
                StartRefresh();
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.EventHandler,
                PackageMerchantReminderFailureReason.EventCallbackException,
                ex
            );
            DisarmEntry();
        }
    }

    private void StartRefresh()
    {
        StopRefresh();
        _refreshCoroutine = StartCoroutine(RefreshWhenPresentationReady(_entryVersion));
    }

    private void StopRefresh()
    {
        if (_refreshCoroutine == null)
            return;

        StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = null;
    }

    private void StopAutoHide()
    {
        if (_autoHideCoroutine == null)
            return;

        StopCoroutine(_autoHideCoroutine);
        _autoHideCoroutine = null;
    }

    private IEnumerator RefreshWhenPresentationReady(int entryVersion)
    {
        // Let the native listener that owns the board mutation run before reading inventory or
        // presentation state. This also guarantees StartCoroutine returns its live handle before
        // the routine can clear it on an immediate no-match outcome.
        yield return null;

        var deadline = Time.realtimeSinceStartup + PresentationReadyTimeoutSeconds;
        while (IsCurrentEntry(entryVersion) && Time.realtimeSinceStartup <= deadline)
        {
            var readOutcome = TryReadMatchingPackage(out var match);
            if (readOutcome == PackageMatchReadOutcome.Failed)
            {
                DisarmEntry();
                yield break;
            }

            if (readOutcome == PackageMatchReadOutcome.NoMatch)
            {
                DisarmEntry();
                yield break;
            }

            if (readOutcome == PackageMatchReadOutcome.Match && IsBoardReady())
            {
                // An already-open stash makes the package visible, so there is nothing to remind.
                // Opening it after presentation is handled by OnStorageToggled and closes the cue.
                if (match.InStash && Data.IsStorageOpen)
                {
                    DisarmEntry();
                    yield break;
                }

                var presentationOutcome = TryReadPresentation(
                    match,
                    out var target,
                    out var anchor
                );
                if (presentationOutcome == PackagePresentationReadOutcome.Failed)
                {
                    DisarmEntry();
                    yield break;
                }

                if (presentationOutcome != PackagePresentationReadOutcome.Ready)
                {
                    yield return null;
                    continue;
                }

                _lastAnchor = anchor;
                _merchantTemplateId = match.MerchantTemplateId;
                _currentPackageInstanceId = match.Card.InstanceId.Value;
                _tooltip?.ShowOrUpdate(target, PackageMerchantReminderText.Resolve(anchor));
                _refreshCoroutine = null;
                yield break;
            }

            yield return null;
        }

        _refreshCoroutine = null;
        if (!IsCurrentEntry(entryVersion))
            yield break;

        ReportDegraded(
            PackageMerchantReminderFailurePhase.PresentationReadiness,
            PackageMerchantReminderFailureReason.ReadyTimeout
        );
        DisarmEntry();
    }

    private bool IsCurrentEntry(int entryVersion) =>
        _entryArmed
        && entryVersion == _entryVersion
        && _merchantCandidateTemplateIds.Count > 0
        && AppState.CurrentState is ChoiceState;

    private static bool IsBoardReady()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        return boardManager is { AllowInteraction: true, StorageMoving: false };
    }

    private PackageMatchReadOutcome TryReadMatchingPackage(out PackageMerchantMatch match)
    {
        match = default;
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var player = Data.Run?.Player;
            if (staticData == null || player == null)
                return PackageMatchReadOutcome.Unavailable;

            if (
                TryReadContainer(
                    player.Hand?.GetItemsAsEnumerable(),
                    staticData,
                    inStash: false,
                    out match
                )
            )
                return PackageMatchReadOutcome.Match;

            return TryReadContainer(
                player.Stash?.GetItemsAsEnumerable(),
                staticData,
                inStash: true,
                out match
            )
                ? PackageMatchReadOutcome.Match
                : PackageMatchReadOutcome.NoMatch;
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.InventoryRead,
                PackageMerchantReminderFailureReason.InventoryReadException,
                ex
            );
            return PackageMatchReadOutcome.Failed;
        }
    }

    private bool TryReadContainer(
        IEnumerable? cards,
        object staticData,
        bool inStash,
        out PackageMerchantMatch match
    )
    {
        match = default;
        if (cards == null)
            return false;

        foreach (var value in cards)
        {
            if (value is not ItemCard card)
                continue;
            if (_soldInstanceIds.Contains(card.InstanceId.Value ?? string.Empty))
                continue;

            var template = BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId);
            var merchantTemplateId = Guid.Empty;
            foreach (var candidateTemplateId in _merchantCandidateTemplateIds)
            {
                if (
                    PackageMerchantIdentity.MatchesMerchantTemplateId(template, candidateTemplateId)
                )
                {
                    merchantTemplateId = candidateTemplateId;
                    break;
                }
            }

            if (merchantTemplateId == Guid.Empty)
                continue;

            match = new PackageMerchantMatch(card, inStash, merchantTemplateId);
            return true;
        }

        return false;
    }

    private PackagePresentationReadOutcome TryReadPresentation(
        PackageMerchantMatch match,
        out Transform target,
        out PackageMerchantReminderAnchor anchor
    )
    {
        target = null!;
        anchor = PackageMerchantReminderAnchor.VisiblePackage;
        try
        {
            return TryResolvePresentation(match, out target, out anchor)
                ? PackagePresentationReadOutcome.Ready
                : PackagePresentationReadOutcome.Unavailable;
        }
        catch (Exception ex)
        {
            ReportDegraded(
                PackageMerchantReminderFailurePhase.PresentationReadiness,
                PackageMerchantReminderFailureReason.PresentationReadException,
                ex
            );
            return PackagePresentationReadOutcome.Failed;
        }
    }

    private static bool TryResolvePresentation(
        PackageMerchantMatch match,
        out Transform target,
        out PackageMerchantReminderAnchor anchor
    )
    {
        target = null!;
        anchor = PackageMerchantReminderAnchor.VisiblePackage;
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            return false;

        if (match.InStash && !Data.IsStorageOpen)
        {
            var storageToy = boardManager.activeStorageToy;
            if (storageToy == null || !storageToy.gameObject.activeInHierarchy)
                return false;

            target = storageToy.transform;
            anchor = PackageMerchantReminderAnchor.ClosedStash;
            return true;
        }

        var itemController =
            Data.CardAndSkillLookup.GetCardController(match.Card) as ItemController;
        if (
            itemController == null
            || !itemController.gameObject.activeInHierarchy
            || !itemController.IsCardVisible
        )
            return false;

        target = itemController.transform;
        return true;
    }

    private void DisarmEntry()
    {
        _entryArmed = false;
        _currentPackageInstanceId = null;
        _refreshCoroutine = null;
        StopAutoHide();
        _tooltip?.Hide();
    }

    private void OnPresentationStarted()
    {
        if (_shownThisEntry || !_entryArmed || _merchantTemplateId == Guid.Empty)
            return;

        _shownThisEntry = true;
        StopAutoHide();
        _autoHideCoroutine = StartCoroutine(AutoHideReminder());
        BppLog.InfoEvent(
            PackageMerchantReminderLogEvents.Shown,
            PackageMerchantReminderLogEvents.ShownMerchantTemplateId.Bind(_merchantTemplateId),
            PackageMerchantReminderLogEvents.ShownAnchor.Bind(_lastAnchor)
        );
    }

    private IEnumerator AutoHideReminder()
    {
        yield return new WaitForSecondsRealtime(ReminderVisibleSeconds);
        _autoHideCoroutine = null;
        _entryArmed = false;
        _currentPackageInstanceId = null;
        _tooltip?.Hide();
    }

    private static void ReportNativeFailure(
        NativeItemTutorialTooltipFailureReason reason,
        Exception? exception
    ) => ReportDegraded(PackageMerchantReminderFailurePhase.NativeTooltip, reason, exception);

    private static void ReportDegraded(
        PackageMerchantReminderFailurePhase phase,
        object reason,
        Exception? exception = null
    )
    {
        var fields = new[]
        {
            PackageMerchantReminderLogEvents.DegradedPhase.Bind(phase),
            PackageMerchantReminderLogEvents.DegradedReasonCode.Bind(reason),
        };
        if (exception == null)
            BppLog.WarnEvent(PackageMerchantReminderLogEvents.Degraded, fields);
        else
            BppLog.WarnEvent(PackageMerchantReminderLogEvents.Degraded, exception, fields);
    }

    private readonly record struct PackageMerchantMatch(
        ItemCard Card,
        bool InStash,
        Guid MerchantTemplateId
    );

    private enum PackageMatchReadOutcome
    {
        Unavailable,
        NoMatch,
        Match,
        Failed,
    }

    private enum PackagePresentationReadOutcome
    {
        Unavailable,
        Ready,
        Failed,
    }
}
