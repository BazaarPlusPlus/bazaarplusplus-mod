#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.RunLogging.Json;

public sealed class RunLogPathLayout
{
    public RunLogPathLayout(string logRootPath)
    {
        LogRootPath = logRootPath ?? throw new ArgumentNullException(nameof(logRootPath));
    }

    public string LogRootPath { get; }

    public string GetDatePartition(DateTimeOffset startedAtUtc)
    {
        return startedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd");
    }

    public string GetRunDirectoryPath(DateTimeOffset startedAtUtc, string runId)
    {
        return Path.Combine(LogRootPath, GetDatePartition(startedAtUtc), runId);
    }

    public string GetMetaFilePath(DateTimeOffset startedAtUtc, string runId)
    {
        return Path.Combine(GetRunDirectoryPath(startedAtUtc, runId), RunLogJsonSchema.MetaFileName);
    }

    public string GetEventsFilePath(DateTimeOffset startedAtUtc, string runId)
    {
        return Path.Combine(
            GetRunDirectoryPath(startedAtUtc, runId),
            RunLogJsonSchema.EventsFileName
        );
    }

    public string GetCheckpointFilePath(DateTimeOffset startedAtUtc, string runId)
    {
        return Path.Combine(
            GetRunDirectoryPath(startedAtUtc, runId),
            RunLogJsonSchema.CheckpointFileName
        );
    }

    public string GetStatusFilePath(DateTimeOffset startedAtUtc, string runId)
    {
        return Path.Combine(GetRunDirectoryPath(startedAtUtc, runId), RunLogJsonSchema.StatusFileName);
    }

    public string GetActiveRunFilePath()
    {
        return Path.Combine(LogRootPath, RunLogJsonSchema.ActiveRunFileName);
    }

    public string GetRunsIndexFilePath()
    {
        return Path.Combine(LogRootPath, RunLogJsonSchema.RunsIndexFileName);
    }
}
