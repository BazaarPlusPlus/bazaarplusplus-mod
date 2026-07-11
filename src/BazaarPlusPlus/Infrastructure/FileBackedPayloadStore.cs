#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace BazaarPlusPlus.Infrastructure;

internal delegate bool TryDeserialize<T>(byte[]? payloadBytes, out T? payload, out string? error)
    where T : class;

internal sealed class FileBackedPayloadStore<T>
    where T : class
{
    private readonly string _rootPath;
    private readonly string _fileSuffix;
    private readonly Func<T, byte[]> _serialize;
    private readonly TryDeserialize<T> _tryDeserialize;
    private readonly string _logTag;
    private readonly string _payloadLogLabel;

    public FileBackedPayloadStore(
        string rootPath,
        string fileSuffix,
        Func<T, byte[]> serialize,
        TryDeserialize<T> tryDeserialize,
        string logTag
    )
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(fileSuffix))
            throw new ArgumentException("File suffix is required.", nameof(fileSuffix));

        _rootPath = rootPath;
        _fileSuffix = fileSuffix;
        _serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
        _tryDeserialize = tryDeserialize ?? throw new ArgumentNullException(nameof(tryDeserialize));
        _logTag = !string.IsNullOrWhiteSpace(logTag)
            ? logTag
            : throw new ArgumentException("Log tag is required.", nameof(logTag));
        _payloadLogLabel = logTag switch
        {
            "CombatReplayPayloadStore" => "replay payload",
            "GhostBattlePayloadStore" => "ghost payload",
            _ => "payload",
        };

        Directory.CreateDirectory(_rootPath);
    }

    public void Save(string battleId, T payload)
    {
        AtomicFileWriter.Write(GetFilePath(battleId), _serialize(payload));
    }

    public T? Load(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return null;

        var filePath = GetFilePath(battleId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var payloadBytes = File.ReadAllBytes(filePath);
            if (_tryDeserialize(payloadBytes, out var payload, out var error))
                return payload;

            BppLog.Warn(
                _logTag,
                $"Skipping invalid {_payloadLogLabel} '{filePath}': {error ?? "unknown_error"}"
            );
            return null;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                _logTag,
                $"Skipping unreadable {_payloadLogLabel} '{filePath}': {ex.Message}"
            );
            return null;
        }
    }

    public bool Exists(string battleId)
    {
        return !string.IsNullOrWhiteSpace(battleId) && File.Exists(GetFilePath(battleId));
    }

    public void Delete(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        var filePath = GetFilePath(battleId);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public IEnumerable<string> ListIds()
    {
        Directory.CreateDirectory(_rootPath);

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, $"*{_fileSuffix}"))
        {
            var fileName = Path.GetFileName(filePath);
            if (
                fileName.EndsWith(_fileSuffix, StringComparison.OrdinalIgnoreCase)
                && fileName.Length > _fileSuffix.Length
            )
            {
                yield return fileName[..^_fileSuffix.Length];
            }
        }
    }

    private string GetFilePath(string battleId)
    {
        return Path.Combine(_rootPath, $"{battleId}{_fileSuffix}");
    }
}
