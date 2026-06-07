#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Executes one dequeued replay control command against the main-thread sink and maps the
/// outcome onto the wire contract. Status codes reuse the existing error code table:
/// 202 accepted (record) / 200 accepted (continue) / 400 <c>invalid</c> /
/// 409 <c>stale-or-unavailable</c> / 503 <c>unavailable</c> / 500 <c>internal</c>.
/// </summary>
public static class BazaarAgentReplayControlProcessor
{
    public static void Process(
        PendingReplayControl pending,
        IBazaarAgentReplayControlSink sink,
        IBazaarAgentLogger logger
    )
    {
        BazaarAgentReplayControlOutcome outcome;
        try
        {
            outcome =
                pending.Kind == BazaarAgentReplayControlKind.Start
                    ? sink.Start(pending.Payload ?? Array.Empty<byte>(), pending.BattleId)
                    : sink.Continue();
        }
        catch (Exception ex)
        {
            logger.Error($"replay control {pending.Kind} threw", ex);
            pending.SetResponse(
                new BazaarAgentServerResponse(500, BuildErrorBody("internal", ex.GetType().Name))
            );
            return;
        }

        pending.SetResponse(MapOutcome(pending.Kind, outcome));
    }

    public static BazaarAgentServerResponse MapOutcome(
        BazaarAgentReplayControlKind kind,
        BazaarAgentReplayControlOutcome outcome
    )
    {
        return outcome.Status switch
        {
            BazaarAgentReplayControlStatus.Accepted
                when kind == BazaarAgentReplayControlKind.Start => new BazaarAgentServerResponse(
                202,
                JsonConvert.SerializeObject(
                    new Dictionary<string, object?>
                    {
                        ["accepted"] = true,
                        ["battleId"] = outcome.BattleId,
                        ["status"] = "recording-started",
                    }
                )
            ),
            BazaarAgentReplayControlStatus.Accepted => new BazaarAgentServerResponse(
                200,
                JsonConvert.SerializeObject(
                    new Dictionary<string, object?>
                    {
                        ["accepted"] = true,
                        ["status"] = "continue-triggered",
                    }
                )
            ),
            BazaarAgentReplayControlStatus.InvalidPayload => new BazaarAgentServerResponse(
                400,
                BuildErrorBody("invalid", outcome.FailureReason)
            ),
            BazaarAgentReplayControlStatus.Rejected => new BazaarAgentServerResponse(
                409,
                BuildErrorBody("stale-or-unavailable", outcome.FailureReason)
            ),
            _ => new BazaarAgentServerResponse(
                503,
                BuildErrorBody("unavailable", outcome.FailureReason)
            ),
        };
    }

    private static string BuildErrorBody(string code, string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = code };
        if (!string.IsNullOrEmpty(details))
            envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope);
    }
}
