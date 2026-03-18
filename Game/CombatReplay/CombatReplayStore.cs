#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayStore
{
    private readonly string _rootPath;

    public CombatReplayStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Replay root path is required.", nameof(rootPath));

        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public void Save(CombatReplayRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(record.ReplayId))
            throw new ArgumentException("Replay id is required.", nameof(record));

        Directory.CreateDirectory(_rootPath);
        var filePath = GetFilePath(record.ReplayId);
        var json = JsonConvert.SerializeObject(record, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }

    public IReadOnlyList<CombatReplayRecord> List()
    {
        Directory.CreateDirectory(_rootPath);

        return Directory
            .EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(LoadFile)
            .Where(record => record != null)
            .OrderByDescending(record => record!.SavedAtUtc)
            .Cast<CombatReplayRecord>()
            .ToList();
    }

    public CombatReplayRecord? Load(string replayId)
    {
        if (string.IsNullOrWhiteSpace(replayId))
            return null;

        var filePath = GetFilePath(replayId);
        if (!File.Exists(filePath))
            return null;

        return LoadFile(filePath);
    }

    private string GetFilePath(string replayId)
    {
        return Path.Combine(_rootPath, $"{replayId}.json");
    }

    private static CombatReplayRecord? LoadFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<CombatReplayRecord>(json);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayStore",
                $"Skipping unreadable replay file '{filePath}': {ex.Message}"
            );
            return null;
        }
    }
}
