#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleSyncService
{
    private const int MaxSyncBattleLimit = 200;
    private readonly HistoryPanelRepository _repository;
    private readonly ModOnlineClient _onlineClient;
    private readonly Func<string?> _playerAccountIdResolver;
    private long _cooldownUntilUtcTicks;
    private int _syncInFlight;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        ModOnlineClient onlineClient,
        Func<string?>? playerAccountIdResolver = null
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
        _playerAccountIdResolver = playerAccountIdResolver ?? ResolvePlayerAccountId;
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        if (Interlocked.CompareExchange(ref _syncInFlight, 1, 0) != 0)
            return GhostBattleSyncResult.Failure(
                "ghost_sync_already_running",
                HistoryPanelGhostSyncReasonCode.QueryFailed
            );

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now.UtcTicks < Interlocked.Read(ref _cooldownUntilUtcTicks))
                return GhostBattleSyncResult.Failure(
                    "ghost_sync_rate_limited",
                    HistoryPanelGhostSyncReasonCode.QueryFailed
                );

            var playerAccountId = _playerAccountIdResolver()?.Trim();
            if (string.IsNullOrWhiteSpace(playerAccountId))
                return GhostBattleSyncResult.Failure(
                    "player_account_id_unavailable",
                    HistoryPanelGhostSyncReasonCode.IdentityUnavailable
                );

            return await QueryAndPersistAsync(playerAccountId!, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _syncInFlight, 0);
        }
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return Failure("battle_id_required", HistoryPanelReplayReasonCode.ReplayUnavailable);
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return Failure(
                "replay_directory_required",
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        var reference = _repository.TryGetGhostBundleReference(battleId);
        if (reference == null)
            return Failure("ghost_battle_missing", HistoryPanelReplayReasonCode.ReplayUnavailable);

        var payloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        if (reference.ReplayState == "local_ready")
        {
            var cached = payloadStore.LoadDetailed(reference.LocalBattleId);
            if (cached.Status == FileBackedPayloadLoadStatus.Loaded)
                return GhostBattleReplayDownloadResult.Success();
        }
        if (reference.ReplayState == "unavailable_payload" || reference.ReplayState == "expired")
            return Failure(
                $"ghost_replay_{reference.ReplayState}",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            );

        var refreshed = false;
        if (reference.DownloadExpiresAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            reference = await RefreshReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            refreshed = true;
            if (reference == null)
            {
                _repository.MarkGhostReplayUnavailable(battleId, "expired", "url_expired");
                return Failure(
                    "ghost_replay_expired",
                    HistoryPanelReplayReasonCode.GhostDownloadFailed
                );
            }
        }

        var client = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var download = await client
            .DownloadBundleAsync(reference.DownloadUrl, cancellationToken)
            .ConfigureAwait(false);
        if (!download.Succeeded && download.StatusCode is 403 or 404 && !refreshed)
        {
            reference = await RefreshReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            refreshed = true;
            if (reference != null)
                download = await client
                    .DownloadBundleAsync(reference.DownloadUrl, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (!download.Succeeded || download.Bytes == null)
        {
            if (download.StatusCode is 403 or 404)
                _repository.MarkGhostReplayUnavailable(battleId, "expired", "object_unavailable");
            return Failure(
                download.Error ?? "ghost_bundle_download_failed",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            );
        }

        var extraction = ExtractPayload(reference!, download.Bytes);
        if (!extraction.Succeeded || extraction.Payload == null)
        {
            _repository.MarkGhostReplayUnavailable(
                battleId,
                "unavailable_payload",
                extraction.Error ?? "payload_invalid"
            );
            return Failure(
                extraction.Error ?? "ghost_bundle_invalid",
                extraction.ReasonCode,
                extraction.Exception
            );
        }

        payloadStore.Save(extraction.Payload);
        _repository.MarkGhostReplayDownloaded(
            battleId,
            HistoryBattlePreviewProjection.CountSnapshots(
                extraction.Payload.BattleManifest.Snapshots.PlayerHand,
                extraction.Payload.BattleManifest.Snapshots.PlayerSkills,
                extraction.Payload.BattleManifest.Snapshots.OpponentHand,
                extraction.Payload.BattleManifest.Snapshots.OpponentSkills
            )
        );
        return GhostBattleReplayDownloadResult.Success();
    }

    private async Task<GhostBattleSyncResult> QueryAndPersistAsync(
        string playerAccountId,
        CancellationToken cancellationToken
    )
    {
        var client = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var result = await client
            .QueryAgainstMeAsync(playerAccountId, MaxSyncBattleLimit, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.StatusCode == 429)
            {
                var cooldown = TimeSpan.FromSeconds(Math.Max(1, result.RetryAfterSeconds ?? 60));
                Interlocked.Exchange(
                    ref _cooldownUntilUtcTicks,
                    DateTimeOffset.UtcNow.Add(cooldown).UtcTicks
                );
            }
            return GhostBattleSyncResult.Failure(
                result.Error ?? "ghost_sync_failed",
                HistoryPanelGhostSyncReasonCode.QueryFailed
            );
        }

        try
        {
            _repository.UpsertGhostBattles(playerAccountId, result.Battles);
            _repository.MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset.UtcNow);
            return GhostBattleSyncResult.Success(result.Battles.Count);
        }
        catch (Exception ex)
        {
            return GhostBattleSyncResult.Failure(
                "ghost_sync_repository_failed",
                HistoryPanelGhostSyncReasonCode.RepositoryFailed,
                ex
            );
        }
    }

    private async Task<GhostBundleReference?> RefreshReferenceAsync(
        GhostBundleReference reference,
        CancellationToken cancellationToken
    )
    {
        var result = await QueryAndPersistAsync(reference.LocalPlayerAccountId, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? _repository.TryGetGhostBundleReference(reference.LocalBattleId)
            : null;
    }

    private static GhostPayloadExtraction ExtractPayload(
        GhostBundleReference reference,
        byte[] bundleBytes
    )
    {
        try
        {
            var opened = BundleV5Codec.Open(bundleBytes);
            if (
                !string.Equals(
                    opened.Manifest.BundleId,
                    reference.BundleId,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    opened.Manifest.Run.PlayerAccountId,
                    reference.UploaderAccountId,
                    StringComparison.Ordinal
                )
            )
                return GhostPayloadExtraction.Failure(
                    "ghost_bundle_identity_mismatch",
                    HistoryPanelReplayReasonCode.GhostBattleMismatch
                );

            if (!RunPayloadV5Codec.TryDecode(opened.RunPayload, out var run, out var decodeReason))
                return GhostPayloadExtraction.Failure(
                    decodeReason ?? "run_payload_invalid",
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );
            if (
                run == null
                || !string.Equals(run.RunId, opened.Manifest.Run.RunId, StringComparison.Ordinal)
                || !string.Equals(
                    run.PlayerAccountId,
                    opened.Manifest.Run.PlayerAccountId,
                    StringComparison.Ordinal
                )
            )
                return GhostPayloadExtraction.Failure(
                    "ghost_run_identity_mismatch",
                    HistoryPanelReplayReasonCode.GhostBattleMismatch
                );

            var battle = run.Battles.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.BattleId,
                    reference.RemoteBattleId,
                    StringComparison.Ordinal
                )
            );
            if (
                battle == null
                || battle.Snapshots == null
                || battle.Replay == null
                || !run.ReplayableBattleIds.Contains(reference.RemoteBattleId)
                || !HasCompleteSnapshots(battle.Snapshots)
                || battle.Replay.SpawnMessageBytes.Length == 0
                || battle.Replay.CombatMessageBytes.Length == 0
                || battle.Replay.DespawnMessageBytes.Length == 0
            )
                return GhostPayloadExtraction.Failure(
                    "ghost_battle_replay_incomplete",
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

            var replay = new PvpReplayPayload
            {
                BattleId = reference.LocalBattleId,
                Version = battle.Replay.Version,
                SpawnMessageBytes = battle.Replay.SpawnMessageBytes.ToArray(),
                CombatMessageBytes = battle.Replay.CombatMessageBytes.ToArray(),
                DespawnMessageBytes = battle.Replay.DespawnMessageBytes.ToArray(),
            };
            _ = new CombatReplayLoader().Load(replay);
            var manifest = BuildLocalPerspectiveManifest(reference, run.RunId, battle);
            return GhostPayloadExtraction.Success(
                new GhostBattlePayload
                {
                    BattleId = reference.LocalBattleId,
                    BattleManifest = manifest,
                    ReplayPayload = replay,
                }
            );
        }
        catch (Exception ex)
        {
            return GhostPayloadExtraction.Failure(
                "ghost_bundle_invalid",
                HistoryPanelReplayReasonCode.GhostArtifactInvalid,
                ex
            );
        }
    }

    private static PvpBattleManifest BuildLocalPerspectiveManifest(
        GhostBundleReference reference,
        string runId,
        RunBattleV5 battle
    ) =>
        new()
        {
            BattleId = reference.LocalBattleId,
            RunId = runId,
            RecordedAtUtc = DateTimeOffset.Parse(battle.Facts.RecordedAtUtc),
            CombatKind = battle.Facts.CombatKind,
            Day = battle.Facts.Day,
            Hour = battle.Facts.Hour,
            EncounterId = battle.Facts.EncounterId,
            Participants = new PvpBattleParticipants
            {
                PlayerName = battle.Participants.Opponent.DisplayName,
                PlayerAccountId = battle.Participants.Opponent.AccountId,
                PlayerHero = battle.Participants.Opponent.HeroName,
                PlayerRank = battle.Participants.Opponent.Rank,
                PlayerRating = battle.Participants.Opponent.Rating,
                PlayerLevel = battle.Participants.Opponent.Level,
                PlayerPrestige = battle.Participants.Opponent.Prestige,
                PlayerIncome = battle.Participants.Opponent.Income,
                PlayerGold = battle.Participants.Opponent.Gold,
                PlayerVictories = battle.Participants.Opponent.Victories,
                OpponentName = battle.Participants.Player.DisplayName,
                OpponentAccountId = battle.Participants.Player.AccountId,
                OpponentHero = battle.Participants.Player.HeroName,
                OpponentRank = battle.Participants.Player.Rank,
                OpponentRating = battle.Participants.Player.Rating,
                OpponentLevel = battle.Participants.Player.Level,
                OpponentPrestige = battle.Participants.Player.Prestige,
                OpponentVictories = battle.Participants.Player.Victories,
            },
            Outcome = new PvpBattleOutcome
            {
                Result = GhostBattleLocalProjector.ProjectResultToLocal(battle.Facts.Result),
                WinnerCombatantId = GhostBattleLocalProjector.ProjectCombatantIdToLocal(
                    battle.Facts.WinnerCombatantId
                ),
                LoserCombatantId = GhostBattleLocalProjector.ProjectCombatantIdToLocal(
                    battle.Facts.LoserCombatantId
                ),
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = BuildCapture(battle.Snapshots!, "opponent_hand"),
                PlayerSkills = BuildCapture(battle.Snapshots!, "opponent_skills"),
                OpponentHand = BuildCapture(battle.Snapshots!, "player_hand"),
                OpponentSkills = BuildCapture(battle.Snapshots!, "player_skills"),
            },
        };

    private static bool HasCompleteSnapshots(BattleCardSnapshotsV5 snapshots)
    {
        var labels = new[] { "player_hand", "player_skills", "opponent_hand", "opponent_skills" };
        return labels.All(label =>
            snapshots.CardSets.Any(set =>
                string.Equals(set.Label, label, StringComparison.Ordinal)
                && !string.Equals(set.Status, "Missing", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    private static PvpBattleCardSetCapture BuildCapture(
        BattleCardSnapshotsV5 snapshots,
        string label
    )
    {
        var source = snapshots.CardSets.First(set =>
            string.Equals(set.Label, label, StringComparison.Ordinal)
        );
        return new PvpBattleCardSetCapture
        {
            Status = ParseEnum(source.Status, PvpBattleCaptureStatus.Missing),
            Source = ParseEnum(source.Source, PvpBattleCaptureSource.Unknown),
            Items = source.Items.Select(MapCard).ToList(),
        };
    }

    private static PvpBattleCardSnapshot MapCard(BattleCardV5 item) =>
        new()
        {
            InstanceId = item.InstanceId,
            TemplateId = item.TemplateId,
            Type = (BazaarGameShared.Domain.Core.Types.ECardType)item.Type,
            Size = (BazaarGameShared.Domain.Core.Types.ECardSize)item.Size,
            Section = item.Section.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EInventorySection?)item.Section.Value
                : null,
            Socket = item.Socket.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EContainerSocketId?)item.Socket.Value
                : null,
            Name = item.Name,
            Tier = item.Tier,
            Enchant = item.Enchant,
            Tags = item.Tags.ToList(),
            Attributes = new Dictionary<string, int>(item.Attributes),
        };

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            ? parsed
            : fallback;

    private static string? ResolvePlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                HistoryPanelLogEvents.GhostIdentityReadFailed,
                ex,
                () =>
                    [
                        HistoryPanelLogEvents.GhostIdentityReasonCode.Bind(
                            HistoryPanelGhostIdentityReasonCode.ClientCacheReadFailed
                        ),
                    ]
            );
            return null;
        }
    }

    private static GhostBattleReplayDownloadResult Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => GhostBattleReplayDownloadResult.Failure(error, reasonCode, exception);
}

internal readonly struct GhostBattleSyncResult
{
    private GhostBattleSyncResult(
        bool succeeded,
        int importedCount,
        string? error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }
    public int ImportedCount { get; }
    public string? Error { get; }
    public HistoryPanelGhostSyncReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static GhostBattleSyncResult Success(int importedCount) =>
        new(true, importedCount, null, HistoryPanelGhostSyncReasonCode.Completed, null);

    public static GhostBattleSyncResult Failure(
        string error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, 0, error, reasonCode, exception);
}

internal readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(
        bool succeeded,
        string? error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }
    public string? Error { get; }
    public HistoryPanelReplayReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static GhostBattleReplayDownloadResult Success() =>
        new(true, null, HistoryPanelReplayReasonCode.Completed, null);

    public static GhostBattleReplayDownloadResult Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, error, reasonCode, exception);
}

internal readonly struct GhostPayloadExtraction
{
    private GhostPayloadExtraction(
        GhostBattlePayload? payload,
        string? error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Payload = payload;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    internal bool Succeeded => Payload != null;
    internal GhostBattlePayload? Payload { get; }
    internal string? Error { get; }
    internal HistoryPanelReplayReasonCode ReasonCode { get; }
    internal Exception? Exception { get; }

    internal static GhostPayloadExtraction Success(GhostBattlePayload payload) =>
        new(payload, null, HistoryPanelReplayReasonCode.Completed, null);

    internal static GhostPayloadExtraction Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(null, error, reasonCode, exception);
}
