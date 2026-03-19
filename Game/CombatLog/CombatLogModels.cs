#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogTiming
{
    internal const double MillisecondsPerFrame = 50d;
}

internal enum CombatLogRowCategory
{
    Event,
    Health,
    Attribute,
    CardAttribute,
    Death,
    Reward,
    System,
    Unknown,
}

internal sealed class CombatLogCardDisplayInfo
{
    public CombatLogCardDisplayInfo(
        string instanceId,
        string? templateId,
        string displayName,
        string? ownerSide = null,
        string? cardType = null
    )
    {
        InstanceId = instanceId;
        TemplateId = templateId;
        DisplayName = displayName;
        OwnerSide = ownerSide;
        CardType = cardType;
    }

    public string InstanceId { get; }

    public string? TemplateId { get; }

    public string DisplayName { get; }

    public string? OwnerSide { get; }

    public string? CardType { get; }
}

internal sealed class CombatLogTimeline
{
    public CombatLogTimeline(
        CombatLogPlaybackPass playbackPass,
        IReadOnlyList<CombatLogFrame> frames,
        IReadOnlyList<CombatLogRow> rows
    )
    {
        PlaybackPass = playbackPass;
        Frames = frames;
        Rows = rows;
    }

    public CombatLogPlaybackPass PlaybackPass { get; }

    public IReadOnlyList<CombatLogFrame> Frames { get; }

    public IReadOnlyList<CombatLogRow> Rows { get; }
}

internal sealed class CombatLogFrame
{
    public CombatLogFrame(
        int frameIndex,
        int framesLeft,
        TimeSpan logicalTime,
        IReadOnlyList<CombatLogEventEntry> events,
        CombatLogSideUpdate? player,
        CombatLogSideUpdate? opponent,
        IReadOnlyList<CombatLogCardUpdateEntry> cardUpdates
    )
    {
        FrameIndex = frameIndex;
        FramesLeft = framesLeft;
        LogicalTime = logicalTime;
        Events = events;
        Player = player;
        Opponent = opponent;
        CardUpdates = cardUpdates;
    }

    public int FrameIndex { get; }

    public int FramesLeft { get; }

    public TimeSpan LogicalTime { get; }

    public IReadOnlyList<CombatLogEventEntry> Events { get; }

    public CombatLogSideUpdate? Player { get; }

    public CombatLogSideUpdate? Opponent { get; }

    public IReadOnlyList<CombatLogCardUpdateEntry> CardUpdates { get; }
}

internal sealed class CombatLogEventEntry
{
    public CombatLogEventEntry(
        string eventType,
        string? executionContextId,
        string? sourceId,
        string? targetId,
        string? sourceDisplayName,
        string? targetDisplayName,
        string text
    )
    {
        EventType = eventType;
        ExecutionContextId = executionContextId;
        SourceId = sourceId;
        TargetId = targetId;
        SourceDisplayName = sourceDisplayName;
        TargetDisplayName = targetDisplayName;
        Text = text;
    }

    public string EventType { get; }

    public string? ExecutionContextId { get; }

    public string? SourceId { get; }

    public string? TargetId { get; }

    public string? SourceDisplayName { get; }

    public string? TargetDisplayName { get; }

    public string Text { get; }
}

internal sealed class CombatLogSideUpdate
{
    public CombatLogSideUpdate(
        string side,
        bool isDead,
        IReadOnlyList<CombatLogHealthAdjustment> healthAdjustments,
        IReadOnlyList<CombatLogAttributeChange> attributes,
        IReadOnlyList<string> details
    )
    {
        Side = side;
        IsDead = isDead;
        HealthAdjustments = healthAdjustments;
        Attributes = attributes;
        Details = details;
    }

    public string Side { get; }

    public bool IsDead { get; }

    public IReadOnlyList<CombatLogHealthAdjustment> HealthAdjustments { get; }

    public IReadOnlyList<CombatLogAttributeChange> Attributes { get; }

    public IReadOnlyList<string> Details { get; }
}

internal sealed class CombatLogHealthAdjustment
{
    public CombatLogHealthAdjustment(string healthType, int amount, bool isCrit, bool isReduced)
    {
        HealthType = healthType;
        Amount = amount;
        IsCrit = isCrit;
        IsReduced = isReduced;
    }

    public string HealthType { get; }

    public int Amount { get; }

    public bool IsCrit { get; }

    public bool IsReduced { get; }
}

internal sealed class CombatLogAttributeChange
{
    public CombatLogAttributeChange(string attributeKey, int previousValue, int currentValue)
    {
        AttributeKey = attributeKey;
        PreviousValue = previousValue;
        CurrentValue = currentValue;
    }

    public string AttributeKey { get; }

    public int PreviousValue { get; }

    public int CurrentValue { get; }
}

internal sealed class CombatLogCardUpdateEntry
{
    public CombatLogCardUpdateEntry(
        CombatLogCardDisplayInfo card,
        IReadOnlyList<CombatLogAttributeChange> attributes,
        IReadOnlyList<string> details
    )
    {
        Card = card;
        Attributes = attributes;
        Details = details;
    }

    public CombatLogCardDisplayInfo Card { get; }

    public IReadOnlyList<CombatLogAttributeChange> Attributes { get; }

    public IReadOnlyList<string> Details { get; }
}

internal sealed class CombatLogRow
{
    public CombatLogRow(
        int frameIndex,
        TimeSpan logicalTime,
        CombatLogRowCategory category,
        string text,
        string? secondaryText = null
    )
    {
        FrameIndex = frameIndex;
        LogicalTime = logicalTime;
        Category = category;
        Text = text;
        SecondaryText = secondaryText;
    }

    public int FrameIndex { get; }

    public TimeSpan LogicalTime { get; }

    public CombatLogRowCategory Category { get; }

    public string Text { get; }

    public string? SecondaryText { get; }
}

internal sealed class CombatLogVisibleRow
{
    public CombatLogVisibleRow(CombatLogRow row, CombatLogRowVisualState visualState)
    {
        Row = row;
        VisualState = visualState;
    }

    public CombatLogRow Row { get; }

    public CombatLogRowVisualState VisualState { get; }
}
