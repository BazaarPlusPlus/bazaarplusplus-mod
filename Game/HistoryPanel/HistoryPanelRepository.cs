#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelRepository
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Converters = new List<JsonConverter> { new StringEnumConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

    private readonly string _databasePath;

    public HistoryPanelRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
    }

    public bool DatabaseExists => File.Exists(_databasePath);

    public IReadOnlyList<HistoryRunRecord> ListRecentRuns(int limit)
    {
        if (!DatabaseExists)
            return Array.Empty<HistoryRunRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                r.run_id,
                r.hero,
                r.game_mode,
                r.started_at_utc,
                r.player_rank,
                r.player_rating,
                r.status AS run_status,
                rs.status AS final_status,
                rs.final_day,
                rs.final_hour,
                rs.max_health AS final_max_health,
                rs.prestige AS final_prestige,
                rs.level AS final_level,
                rs.income AS final_income,
                rs.gold AS final_gold,
                rs.victories,
                rs.losses,
                rs.ended_at_utc,
                cp.day AS checkpoint_day,
                cp.hour AS checkpoint_hour,
                cp.max_health AS checkpoint_max_health,
                cp.prestige AS checkpoint_prestige,
                cp.level AS checkpoint_level,
                cp.income AS checkpoint_income,
                cp.gold AS checkpoint_gold,
                cp.last_seen_at_utc,
                COUNT(pb.battle_id) AS battle_count
            FROM {RunLogSqliteSchema.RunsTableName} AS r
            LEFT JOIN {RunLogSqliteSchema.RunStatusTableName} AS rs
                ON rs.run_id = r.run_id
            LEFT JOIN {RunLogSqliteSchema.RunCheckpointsTableName} AS cp
                ON cp.run_id = r.run_id
            LEFT JOIN {RunLogSqliteSchema.PvpBattlesTableName} AS pb
                ON pb.run_id = r.run_id
            GROUP BY
                r.run_id,
                r.hero,
                r.game_mode,
                r.started_at_utc,
                r.player_rank,
                r.player_rating,
                r.status,
                rs.status,
                rs.final_day,
                rs.final_hour,
                rs.max_health,
                rs.prestige,
                rs.level,
                rs.income,
                rs.gold,
                rs.victories,
                rs.losses,
                rs.ended_at_utc,
                cp.day,
                cp.hour,
                cp.max_health,
                cp.prestige,
                cp.level,
                cp.income,
                cp.gold,
                cp.last_seen_at_utc
            ORDER BY
                COALESCE(rs.ended_at_utc, cp.last_seen_at_utc, r.started_at_utc) DESC,
                r.run_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryRunRecord>();
        while (reader.Read())
        {
            var startedAt = DateTimeOffset.Parse(
                reader.GetString(reader.GetOrdinal("started_at_utc"))
            );
            var endedAt = GetNullableDateTimeOffset(reader, "ended_at_utc");
            var victories = GetNullableInt32(reader, "victories");
            var losses = GetNullableInt32(reader, "losses");
            var finalDay =
                GetNullableInt32(reader, "final_day") ?? GetNullableInt32(reader, "checkpoint_day");
            var finalHour =
                GetNullableInt32(reader, "final_hour")
                ?? GetNullableInt32(reader, "checkpoint_hour");
            var lastSeen =
                endedAt ?? GetNullableDateTimeOffset(reader, "last_seen_at_utc") ?? startedAt;
            var rawStatus =
                GetNullableString(reader, "final_status")
                ?? reader.GetString(reader.GetOrdinal("run_status"));

            records.Add(
                new HistoryRunRecord(
                    reader.GetString(reader.GetOrdinal("run_id")),
                    reader.GetString(reader.GetOrdinal("hero")),
                    reader.GetString(reader.GetOrdinal("game_mode")),
                    startedAt,
                    endedAt,
                    lastSeen,
                    finalDay,
                    finalHour,
                    GetNullableInt32(reader, "final_max_health")
                        ?? GetNullableInt32(reader, "checkpoint_max_health"),
                    GetNullableInt32(reader, "final_prestige")
                        ?? GetNullableInt32(reader, "checkpoint_prestige"),
                    GetNullableInt32(reader, "final_level")
                        ?? GetNullableInt32(reader, "checkpoint_level"),
                    GetNullableInt32(reader, "final_income")
                        ?? GetNullableInt32(reader, "checkpoint_income"),
                    GetNullableInt32(reader, "final_gold")
                        ?? GetNullableInt32(reader, "checkpoint_gold"),
                    GetNullableString(reader, "player_rank"),
                    GetNullableInt32(reader, "player_rating"),
                    victories,
                    losses,
                    rawStatus,
                    reader.GetInt32(reader.GetOrdinal("battle_count"))
                )
            );
        }

        return records;
    }

    public IReadOnlyList<HistoryBattleRecord> ListBattlesByRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return Array.Empty<HistoryBattleRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                battle_id,
                run_id,
                recorded_at_utc,
                day,
                hour,
                encounter_id,
                player_rank,
                player_rating,
                opponent_name,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_account_id,
                combat_kind,
                result,
                player_hand_json,
                player_skills_json,
                opponent_hand_json,
                opponent_skills_json
            FROM {RunLogSqliteSchema.PvpBattlesTableName}
            WHERE run_id = $runId
            ORDER BY recorded_at_utc DESC, battle_id DESC;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryBattleRecord>();
        while (reader.Read())
        {
            var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
            try
            {
                var playerHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_hand_json"))
                );
                var playerSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_skills_json"))
                );
                var opponentHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_hand_json"))
                );
                var opponentSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_skills_json"))
                );

                records.Add(
                    new HistoryBattleRecord(
                        battleId,
                        reader.GetString(reader.GetOrdinal("run_id")),
                        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at_utc"))),
                        GetNullableInt32(reader, "day"),
                        GetNullableInt32(reader, "hour"),
                        GetNullableString(reader, "encounter_id"),
                        GetNullableString(reader, "player_rank"),
                        GetNullableInt32(reader, "player_rating"),
                        GetNullableString(reader, "opponent_name"),
                        GetNullableString(reader, "opponent_hero"),
                        GetNullableString(reader, "opponent_rank"),
                        GetNullableInt32(reader, "opponent_rating"),
                        GetNullableString(reader, "opponent_account_id"),
                        GetNullableString(reader, "combat_kind"),
                        GetNullableString(reader, "result"),
                        BuildSnapshotSummary(
                            playerHand,
                            playerSkills,
                            opponentHand,
                            opponentSkills
                        ),
                        BuildPreviewData(playerHand, playerSkills, opponentHand, opponentSkills)
                    )
                );
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "HistoryPanelRepository",
                    $"Skipping unreadable battle history row '{battleId}': {ex.Message}"
                );
            }
        }

        return records;
    }

    public IReadOnlyList<string> ListBattleIdsByRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return Array.Empty<string>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT battle_id
            FROM {RunLogSqliteSchema.PvpBattlesTableName}
            WHERE run_id = $runId
            ORDER BY recorded_at_utc DESC, battle_id DESC;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var battleIds = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                battleIds.Add(reader.GetString(0));
        }

        return battleIds;
    }

    public void DeleteRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteBattles = connection.CreateCommand())
        {
            deleteBattles.Transaction = transaction;
            deleteBattles.CommandTimeout = 2;
            deleteBattles.CommandText =
                $"DELETE FROM {RunLogSqliteSchema.PvpBattlesTableName} WHERE run_id = $runId;";
            deleteBattles.Parameters.AddWithValue("$runId", runId);
            deleteBattles.ExecuteNonQuery();
        }

        using (var deleteRun = connection.CreateCommand())
        {
            deleteRun.Transaction = transaction;
            deleteRun.CommandTimeout = 2;
            deleteRun.CommandText =
                $"DELETE FROM {RunLogSqliteSchema.RunsTableName} WHERE run_id = $runId;";
            deleteRun.Parameters.AddWithValue("$runId", runId);
            deleteRun.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string BuildSnapshotSummary(
        PvpBattleCardSetCapture playerHand,
        PvpBattleCardSetCapture playerSkills,
        PvpBattleCardSetCapture opponentHand,
        PvpBattleCardSetCapture opponentSkills
    )
    {
        var playerItems = CountSnapshotItems(playerHand);
        var playerSkillCount = CountSnapshotItems(playerSkills);
        var opponentItems = CountSnapshotItems(opponentHand);
        var opponentSkillCount = CountSnapshotItems(opponentSkills);
        return $"YOU {playerItems} {Pluralize(playerItems, "item", "items")} · {playerSkillCount} {Pluralize(playerSkillCount, "skill", "skills")}"
            + $"  |  OPP {opponentItems} {Pluralize(opponentItems, "item", "items")} · {opponentSkillCount} {Pluralize(opponentSkillCount, "skill", "skills")}";
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }

    private static HistoryBattlePreviewData BuildPreviewData(
        PvpBattleCardSetCapture playerHand,
        PvpBattleCardSetCapture playerSkills,
        PvpBattleCardSetCapture opponentHand,
        PvpBattleCardSetCapture opponentSkills
    )
    {
        var playerBoard = BuildPreviewBoard(playerHand, playerSkills);
        var opponentBoard = BuildPreviewBoard(opponentHand, opponentSkills);
        return new HistoryBattlePreviewData(playerBoard, opponentBoard);
    }

    private static PreviewBoardModel BuildPreviewBoard(
        PvpBattleCardSetCapture itemCapture,
        PvpBattleCardSetCapture skillCapture
    )
    {
        var model = new PreviewBoardModel
        {
            ItemCards = PreviewCardSpecFilter.FilterLocallyRenderable(
                BuildPreviewCardSpecs(itemCapture?.Items, isSkill: false)
            ),
            SkillCards = PreviewCardSpecFilter.FilterLocallyRenderable(
                BuildPreviewCardSpecs(skillCapture?.Items, isSkill: true)
            ),
            Metadata = new Dictionary<string, string>(),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }

    private static List<PreviewCardSpec> BuildPreviewCardSpecs(
        IEnumerable<CombatReplayCardSnapshot>? snapshots,
        bool isSkill
    )
    {
        var specs = new List<PreviewCardSpec>();
        if (snapshots == null)
            return specs;

        foreach (
            var snapshot in snapshots
                .Select((snapshot, index) => new { snapshot, index })
                .OrderBy(entry => entry.snapshot?.Socket.HasValue == true ? 0 : 1)
                .ThenBy(entry =>
                    entry.snapshot?.Socket.HasValue == true
                        ? (int)entry.snapshot.Socket!.Value
                        : int.MaxValue
                )
                .ThenBy(entry => entry.index)
                .Select(entry => entry.snapshot)
        )
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;

            var spec = BuildPreviewCardSpec(snapshot, isSkill);
            if (spec != null)
                specs.Add(spec);
        }

        return specs;
    }

    private static PreviewCardSpec? BuildPreviewCardSpec(
        CombatReplayCardSnapshot snapshot,
        bool isSkill
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        var attributes = new Dictionary<int, int>();
        if (snapshot.Attributes != null)
        {
            foreach (var pair in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        pair.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                {
                    attributes[(int)attributeType] = pair.Value;
                }
            }
        }

        return new PreviewCardSpec
        {
            TemplateId = snapshot.TemplateId,
            SourceName = snapshot.Name ?? string.Empty,
            Tier = ParseTier(snapshot.Tier),
            Size = isSkill ? 1 : ParseSize(snapshot.Size),
            Enchant = string.IsNullOrWhiteSpace(snapshot.Enchant) ? "None" : snapshot.Enchant!,
            Attributes = attributes,
        };
    }

    private static PvpBattleCardSetCapture DeserializeCapture(string json)
    {
        return JsonConvert.DeserializeObject<PvpBattleCardSetCapture>(json, SerializerSettings)
            ?? new PvpBattleCardSetCapture();
    }

    private static int ParseTier(string? value)
    {
        return
            !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<ETier>(value, ignoreCase: false, out var tier)
            ? (int)tier
            : 0;
    }

    private static int ParseSize(ECardSize size)
    {
        return size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
    }

    private static int CountSnapshotItems(PvpBattleCardSetCapture? capture)
    {
        return capture?.Items?.Count ?? 0;
    }

    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? SafeGetNullableString(SqliteDataReader reader, string columnName)
    {
        try
        {
            return GetNullableString(reader, columnName);
        }
        catch
        {
            return null;
        }
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(
        SqliteDataReader reader,
        string columnName
    )
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    }
}

internal sealed class HistoryRunRecord
{
    public HistoryRunRecord(
        string runId,
        string hero,
        string gameMode,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset lastSeenAtUtc,
        int? finalDay,
        int? finalHour,
        int? maxHealth,
        int? prestige,
        int? level,
        int? income,
        int? gold,
        string? playerRank,
        int? playerRating,
        int? victories,
        int? losses,
        string rawStatus,
        int battleCount
    )
    {
        RunId = runId;
        Hero = hero;
        GameMode = gameMode;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
        FinalDay = finalDay;
        FinalHour = finalHour;
        MaxHealth = maxHealth;
        Prestige = prestige;
        Level = level;
        Income = income;
        Gold = gold;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        Victories = victories;
        Losses = losses;
        RawStatus = rawStatus;
        BattleCount = battleCount;
    }

    public string RunId { get; }

    public string Hero { get; }

    public string GameMode { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public DateTimeOffset LastSeenAtUtc { get; }

    public int? FinalDay { get; }

    public int? FinalHour { get; }

    public int? MaxHealth { get; }

    public int? Prestige { get; }

    public int? Level { get; }

    public int? Income { get; }

    public int? Gold { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? Victories { get; }

    public int? Losses { get; }

    public string RawStatus { get; }

    public int BattleCount { get; }
}

internal sealed class HistoryBattleRecord
{
    public HistoryBattleRecord(
        string battleId,
        string runId,
        DateTimeOffset recordedAtUtc,
        int? day,
        int? hour,
        string? encounterId,
        string? playerRank,
        int? playerRating,
        string? opponentName,
        string? opponentHero,
        string? opponentRank,
        int? opponentRating,
        string? opponentAccountId,
        string? combatKind,
        string? result,
        string snapshotSummary,
        HistoryBattlePreviewData previewData
    )
    {
        BattleId = battleId;
        RunId = runId;
        RecordedAtUtc = recordedAtUtc;
        Day = day;
        Hour = hour;
        EncounterId = encounterId;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        OpponentName = opponentName;
        OpponentHero = opponentHero;
        OpponentRank = opponentRank;
        OpponentRating = opponentRating;
        OpponentAccountId = opponentAccountId;
        CombatKind = combatKind;
        Result = result;
        SnapshotSummary = snapshotSummary;
        PreviewData = previewData;
    }

    public string BattleId { get; }

    public string RunId { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public int? Day { get; }

    public int? Hour { get; }

    public string? EncounterId { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public string? OpponentName { get; }

    public string? OpponentHero { get; }

    public string? OpponentRank { get; }

    public int? OpponentRating { get; }

    public string? OpponentAccountId { get; }

    public string? CombatKind { get; }

    public string? Result { get; }

    public string SnapshotSummary { get; }

    public HistoryBattlePreviewData PreviewData { get; }
}

internal sealed class HistoryBattlePreviewData
{
    private static readonly PreviewBoardModel EmptyBoard = new PreviewBoardModel
    {
        ItemCards = new List<PreviewCardSpec>(),
        SkillCards = new List<PreviewCardSpec>(),
        Metadata = new Dictionary<string, string>(),
        Signature = string.Empty,
    };

    public HistoryBattlePreviewData(PreviewBoardModel playerBoard, PreviewBoardModel opponentBoard)
    {
        PlayerBoard = playerBoard ?? CloneBoard(EmptyBoard);
        OpponentBoard = opponentBoard ?? CloneBoard(EmptyBoard);
    }

    public PreviewBoardModel PlayerBoard { get; }

    public PreviewBoardModel OpponentBoard { get; }

    public bool HasRenderablePlayerBoard => CountRenderableCards(PlayerBoard) > 0;

    public bool HasRenderableOpponentBoard => CountRenderableCards(OpponentBoard) > 0;

    public bool HasRenderableCards => HasRenderablePlayerBoard || HasRenderableOpponentBoard;

    public HistoryBattlePreviewData PlayerOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneBoard(OpponentBoard));
    }

    public HistoryBattlePreviewData PlayerHandOnly()
    {
        return new HistoryBattlePreviewData(CloneItemBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentHandOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneItemBoard(OpponentBoard));
    }

    private static int CountRenderableCards(PreviewBoardModel board)
    {
        if (board == null)
            return 0;

        return (board.ItemCards?.Count ?? 0) + (board.SkillCards?.Count ?? 0);
    }

    private static PreviewBoardModel CloneBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards =
                source?.SkillCards != null
                    ? new List<PreviewCardSpec>(source.SkillCards)
                    : new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }

    private static PreviewBoardModel CloneItemBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards = new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }
}
