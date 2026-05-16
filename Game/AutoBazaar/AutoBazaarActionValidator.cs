#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal enum AutoBazaarValidationCode
{
    Ok,
    Invalid,
    StaleOrUnavailable,
    Cooldown,
    Unavailable,
}

internal readonly record struct AutoBazaarValidationResult(
    AutoBazaarValidationCode Code,
    int HttpStatus,
    string? Details,
    IReadOnlyDictionary<string, object?>? Extra);

internal static class AutoBazaarActionValidator
{
    // Hardcoded sets — no dependency on game enums.
    private static readonly HashSet<string> _validHeroes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common", "Pygmalien", "Vanessa", "Stelle", "Jules", "Dooley", "Mak", "Karnok",
    };

    private static readonly HashSet<string> _validPlayModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unranked", "Ranked",
    };

    // Kinds that carry a (CardInstanceId, TargetSection, TargetSockets) triplet.
    private static readonly HashSet<AutoBazaarActionKind> _cardBearingKinds = new()
    {
        AutoBazaarActionKind.SelectItem,
        AutoBazaarActionKind.SelectSkill,
        AutoBazaarActionKind.SelectEncounter,
        AutoBazaarActionKind.CommitToPedestal,
        AutoBazaarActionKind.MoveItem,
        AutoBazaarActionKind.SellItem,
    };

    private static AutoBazaarValidationResult Ok()
        => new(AutoBazaarValidationCode.Ok, 200, null, null);

    private static AutoBazaarValidationResult Fail(
        AutoBazaarValidationCode code,
        int httpStatus,
        string details,
        IReadOnlyDictionary<string, object?>? extra = null)
        => new(code, httpStatus, details, extra);

    public static AutoBazaarValidationResult Validate(
        AutoBazaarContextSnapshot snapshot,
        AutoBazaarAction action,
        double cooldownRemainingSeconds)
    {
        var kind = action.ActionKind;

        // ── Rule 1: known actionKind ──────────────────────────────────────────
        if (!Enum.IsDefined(typeof(AutoBazaarActionKind), kind))
            return Fail(AutoBazaarValidationCode.Invalid, 400, "unknown actionKind");

        // ── Rule 2: actionKind in AvailableActions (Wait exempt) ──────────────
        if (kind != AutoBazaarActionKind.Wait)
        {
            var found = false;
            foreach (var opt in snapshot.Context.AvailableActions)
            {
                if (opt.ActionKind == kind) { found = true; break; }
            }
            if (!found)
                return Fail(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                    "actionKind not in availableActions",
                    new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId });
        }

        // ── Rule 3: card-bearing exact match ──────────────────────────────────
        AutoBazaarDecisionOption? matchedOption = null;

        if (_cardBearingKinds.Contains(kind))
        {
            foreach (var opt in snapshot.Context.AvailableActions)
            {
                if (opt.ActionKind == kind
                    && opt.CardInstanceId == action.CardInstanceId
                    && opt.TargetSection == action.TargetSection
                    && SocketsEqual(opt.TargetSockets, action.TargetSockets))
                {
                    matchedOption = opt;
                    break;
                }
            }
            if (matchedOption is null)
                return Fail(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                    "no matching option for card-bearing action",
                    new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId });
        }

        // ── Rule 4: Hero / PlayMode (StartOrContinueRun only) ─────────────────
        if (kind == AutoBazaarActionKind.StartOrContinueRun)
        {
            if (action.Hero is { } hero && !_validHeroes.Contains(hero))
                return Fail(AutoBazaarValidationCode.Invalid, 400, "unknown hero");

            if (action.PlayMode is { } playMode && !_validPlayModes.Contains(playMode))
                return Fail(AutoBazaarValidationCode.Invalid, 400, "unknown playMode");
        }

        // ── Rule 5: CanSelect != false ────────────────────────────────────────
        if (matchedOption is not null
            && kind is AutoBazaarActionKind.SelectItem
                    or AutoBazaarActionKind.SelectSkill
                    or AutoBazaarActionKind.SelectEncounter
                    or AutoBazaarActionKind.CommitToPedestal)
        {
            if (matchedOption.Card?.CanSelect == false)
                return Fail(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                    "option marked CanSelect=false");
        }

        // ── Rule 6: CanSell == true ────────────────────────────────────────────
        if (kind == AutoBazaarActionKind.SellItem && matchedOption is not null)
        {
            if (matchedOption.Card?.CanSell != true)
                return Fail(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                    "option not sellable");
        }

        // ── Rule 7: forTickId match ────────────────────────────────────────────
        if (action.ForTickId is { } want && want != snapshot.TickId)
            return Fail(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                "stale tickId",
                new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId });

        // ── Rule 8: cooldown (Wait exempt) ────────────────────────────────────
        if (kind != AutoBazaarActionKind.Wait && cooldownRemainingSeconds > 0)
            return Fail(AutoBazaarValidationCode.Cooldown, 429,
                "action min-delay not yet elapsed",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = cooldownRemainingSeconds });

        return Ok();
    }

    private static bool SocketsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
