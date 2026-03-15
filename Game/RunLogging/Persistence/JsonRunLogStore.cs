#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Game.RunLogging.Json;
using BazaarPlusPlus.Game.RunLogging.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

public sealed class JsonRunLogStore : IRunLogStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

    private readonly RunLogPathLayout _pathLayout;
    private readonly Dictionary<string, RunLogLocation> _knownRunLocations = new(
        StringComparer.Ordinal
    );

    public JsonRunLogStore(string logRootPath)
    {
        if (string.IsNullOrWhiteSpace(logRootPath))
            throw new ArgumentException("Log root path is required.", nameof(logRootPath));

        _pathLayout = new RunLogPathLayout(logRootPath);
    }

    public RunLogSessionState? TryResumeActiveRun()
    {
        var activeRun = ReadActiveRun();
        if (activeRun == null)
            return null;

        var runDirectoryPath = Path.Combine(_pathLayout.LogRootPath, activeRun.RunPath);
        var location = new RunLogLocation(
            activeRun.RunId,
            activeRun.DatePartition,
            runDirectoryPath,
            Path.Combine(runDirectoryPath, RunLogJsonSchema.MetaFileName),
            Path.Combine(runDirectoryPath, RunLogJsonSchema.EventsFileName),
            Path.Combine(runDirectoryPath, RunLogJsonSchema.CheckpointFileName),
            Path.Combine(runDirectoryPath, RunLogJsonSchema.StatusFileName)
        );
        _knownRunLocations[activeRun.RunId] = location;

        if (File.Exists(location.StatusFilePath))
        {
            ClearActiveRun(activeRun.RunId);
            return null;
        }

        var metadata = ReadJson<RunLogCreateRequest>(location.MetaFilePath);
        if (metadata == null)
            return null;

        var checkpoint = ReadJson<RunLogCheckpoint>(location.CheckpointFilePath);
        if (checkpoint?.Completed == true)
            return null;

        return new RunLogSessionState
        {
            RunId = activeRun.RunId,
            SchemaVersion = checkpoint?.SchemaVersion ?? metadata.SchemaVersion,
            StartedAtUtc = metadata.StartedAtUtc,
            LastSeenAtUtc = checkpoint?.LastSeenAtUtc ?? activeRun.UpdatedAtUtc,
            LastSeq = checkpoint?.LastSeq ?? activeRun.LastSeq,
            Day = checkpoint?.Day ?? metadata.Day,
            Hour = checkpoint?.Hour ?? metadata.Hour,
            State = checkpoint?.State,
            CurrentEncounterId = checkpoint?.CurrentEncounterId,
            LastStateFingerprint = checkpoint?.LastStateFingerprint,
            LastSelectionFingerprint = checkpoint?.LastSelectionFingerprint,
            PendingSelectionSeq = checkpoint?.PendingSelectionSeq,
            Completed = false,
        };
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        var location = CreateLocation(request.StartedAtUtc, request.RunId);
        Directory.CreateDirectory(location.RunDirectoryPath);

        WriteJsonAtomic(
            location.MetaFilePath,
            new
            {
                request.SchemaVersion,
                request.RunId,
                request.StartedAtUtc,
                request.Hero,
                request.GameMode,
                request.Day,
                request.Hour,
                request.Seed,
                request.Status,
            }
        );

        UpdateActiveRun(
            new ActiveRunRecord
            {
                SchemaVersion = request.SchemaVersion,
                RunId = request.RunId,
                DatePartition = location.DatePartition,
                RunPath = Path.Combine(location.DatePartition, request.RunId),
                LastSeq = 0,
                UpdatedAtUtc = request.StartedAtUtc,
            }
        );

        return new RunLogSessionState
        {
            RunId = request.RunId,
            SchemaVersion = request.SchemaVersion,
            StartedAtUtc = request.StartedAtUtc,
            LastSeenAtUtc = request.StartedAtUtc,
            LastSeq = 0,
            Day = request.Day,
            Hour = request.Hour,
            Completed = false,
        };
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        var location = ResolveLocation(runId);
        Directory.CreateDirectory(location.RunDirectoryPath);
        File.AppendAllText(
            location.EventsFilePath,
            JsonConvert.SerializeObject(entry, SerializerSettings) + Environment.NewLine
        );
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        var location = ResolveLocation(runId);
        WriteJsonAtomic(location.CheckpointFilePath, checkpoint);
        UpdateActiveRun(
            new ActiveRunRecord
            {
                SchemaVersion = checkpoint.SchemaVersion,
                RunId = checkpoint.RunId,
                DatePartition = location.DatePartition,
                RunPath = Path.Combine(location.DatePartition, checkpoint.RunId),
                LastSeq = checkpoint.LastSeq,
                UpdatedAtUtc = checkpoint.LastSeenAtUtc,
            }
        );
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        var location = ResolveLocation(runId);
        WriteJsonAtomic(location.StatusFilePath, completion);
        UpdateCheckpointCompleted(location.CheckpointFilePath, completion.EndedAtUtc);
        ClearActiveRun(runId);
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        var location = ResolveLocation(runId);
        WriteJsonAtomic(location.StatusFilePath, abandonment);
        UpdateCheckpointCompleted(location.CheckpointFilePath, abandonment.EndedAtUtc);
        ClearActiveRun(runId);
    }

    private RunLogLocation CreateLocation(DateTimeOffset startedAtUtc, string runId)
    {
        var datePartition = _pathLayout.GetDatePartition(startedAtUtc);
        var location = new RunLogLocation(
            runId,
            datePartition,
            _pathLayout.GetRunDirectoryPath(startedAtUtc, runId),
            _pathLayout.GetMetaFilePath(startedAtUtc, runId),
            _pathLayout.GetEventsFilePath(startedAtUtc, runId),
            _pathLayout.GetCheckpointFilePath(startedAtUtc, runId),
            _pathLayout.GetStatusFilePath(startedAtUtc, runId)
        );
        _knownRunLocations[runId] = location;
        return location;
    }

    private RunLogLocation ResolveLocation(string runId)
    {
        if (_knownRunLocations.TryGetValue(runId, out var location))
            return location;

        var activeRun = ReadActiveRun();
        if (activeRun != null && string.Equals(activeRun.RunId, runId, StringComparison.Ordinal))
        {
            var runDirectoryPath = Path.Combine(_pathLayout.LogRootPath, activeRun.RunPath);
            location = new RunLogLocation(
                runId,
                activeRun.DatePartition,
                runDirectoryPath,
                Path.Combine(runDirectoryPath, RunLogJsonSchema.MetaFileName),
                Path.Combine(runDirectoryPath, RunLogJsonSchema.EventsFileName),
                Path.Combine(runDirectoryPath, RunLogJsonSchema.CheckpointFileName),
                Path.Combine(runDirectoryPath, RunLogJsonSchema.StatusFileName)
            );
            _knownRunLocations[runId] = location;
            return location;
        }

        throw new InvalidOperationException($"Run location not found for {runId}.");
    }

    private ActiveRunRecord? ReadActiveRun()
    {
        var path = _pathLayout.GetActiveRunFilePath();
        if (!File.Exists(path))
            return null;

        return JsonConvert.DeserializeObject<ActiveRunRecord>(File.ReadAllText(path), SerializerSettings);
    }

    private void UpdateActiveRun(ActiveRunRecord activeRun)
    {
        WriteJsonAtomic(_pathLayout.GetActiveRunFilePath(), activeRun);
    }

    private void ClearActiveRun(string runId)
    {
        var path = _pathLayout.GetActiveRunFilePath();
        if (!File.Exists(path))
            return;

        var activeRun = ReadActiveRun();
        if (activeRun == null || !string.Equals(activeRun.RunId, runId, StringComparison.Ordinal))
            return;

        File.Delete(path);
    }

    private static void WriteJsonAtomic(string path, object value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonConvert.SerializeObject(value, SerializerSettings));

        if (File.Exists(path))
            File.Replace(tempPath, path, null, true);
        else
            File.Move(tempPath, path);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), SerializerSettings);
    }

    private void UpdateCheckpointCompleted(string checkpointPath, DateTimeOffset endedAtUtc)
    {
        var checkpoint = ReadJson<RunLogCheckpoint>(checkpointPath);
        if (checkpoint == null)
            return;

        checkpoint.Completed = true;
        checkpoint.LastSeenAtUtc = endedAtUtc;
        WriteJsonAtomic(checkpointPath, checkpoint);
    }

    private sealed class RunLogLocation
    {
        public RunLogLocation(
            string runId,
            string datePartition,
            string runDirectoryPath,
            string metaFilePath,
            string eventsFilePath,
            string checkpointFilePath,
            string statusFilePath
        )
        {
            RunId = runId;
            DatePartition = datePartition;
            RunDirectoryPath = runDirectoryPath;
            MetaFilePath = metaFilePath;
            EventsFilePath = eventsFilePath;
            CheckpointFilePath = checkpointFilePath;
            StatusFilePath = statusFilePath;
        }

        public string RunId { get; }

        public string DatePartition { get; }

        public string RunDirectoryPath { get; }

        public string MetaFilePath { get; }

        public string EventsFilePath { get; }

        public string CheckpointFilePath { get; }

        public string StatusFilePath { get; }
    }

    private sealed class ActiveRunRecord
    {
        public int SchemaVersion { get; set; }

        public string RunId { get; set; } = string.Empty;

        public string DatePartition { get; set; } = string.Empty;

        public string RunPath { get; set; } = string.Empty;

        public long LastSeq { get; set; }

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
