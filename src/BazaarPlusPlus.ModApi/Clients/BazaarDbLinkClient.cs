#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

public enum BazaarDbLinkOutcome
{
    Linked,
    InvalidOrExpired,
    AlreadyLinked,
    MissingFields,
    ServerError,
    Transport,
}

public readonly struct BazaarDbLinkResult
{
    private BazaarDbLinkResult(BazaarDbLinkOutcome outcome, int? statusCode, string? error)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        Error = error;
    }

    public BazaarDbLinkOutcome Outcome { get; }

    public int? StatusCode { get; }

    public string? Error { get; }

    public bool Succeeded => Outcome == BazaarDbLinkOutcome.Linked;

    public static BazaarDbLinkResult Linked() => new(BazaarDbLinkOutcome.Linked, 200, null);

    public static BazaarDbLinkResult From(BazaarDbLinkOutcome o, int? status, string? error) =>
        new(o, status, error);
}

/// <summary>
/// Redeems a one-time BazaarDB profile-link code. Posts to a fixed full URI, no auth header.
/// Code is trimmed only (case-sensitive alphabet). 409 means already linked to a different
/// BazaarDB user and is permanent.
/// </summary>
public sealed class BazaarDbLinkClient
{
    public const string DefaultRedeemEndpoint = "https://bazaardb.gg/api/profile/link/redeem";

    private readonly HttpClient _httpClient;
    private readonly Uri _redeemEndpoint;

    public BazaarDbLinkClient(HttpClient httpClient, Uri redeemEndpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redeemEndpoint = redeemEndpoint ?? throw new ArgumentNullException(nameof(redeemEndpoint));
    }

    public async Task<BazaarDbLinkResult> RedeemAsync(
        string code,
        string accountId,
        CancellationToken cancellationToken
    )
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0 || string.IsNullOrWhiteSpace(accountId))
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.MissingFields, 400, "missing_field");

        var payload = new BazaarDbProfileLinkRedeemRequest
        {
            Code = trimmedCode,
            AccountId = accountId,
        };
        try
        {
            var result = await ModApiJsonPost
                .PostJsonAsync(_httpClient, _redeemEndpoint.AbsoluteUri, payload, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
                return BazaarDbLinkResult.Linked();

            return Classify(result.StatusCode, result.FailureBody!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BazaarDbLinkResult.From(
                BazaarDbLinkOutcome.Transport,
                null,
                ModApiErrorFormatter.Truncate(ex.Message)
            );
        }
    }

    private static BazaarDbLinkResult Classify(int statusCode, string failureBody)
    {
        var error = ExtractErrorCode(failureBody);
        var detail = ModApiErrorFormatter.FormatHttpFailure(statusCode, failureBody);

        if (error == "already_linked" || statusCode == 409)
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.AlreadyLinked, statusCode, detail);
        if (error == "invalid_or_expired")
            return BazaarDbLinkResult.From(
                BazaarDbLinkOutcome.InvalidOrExpired,
                statusCode,
                detail
            );
        if (error != null && error.StartsWith("Missing", StringComparison.OrdinalIgnoreCase))
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.MissingFields, statusCode, detail);
        if (statusCode >= 500)
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.ServerError, statusCode, detail);

        return BazaarDbLinkResult.From(BazaarDbLinkOutcome.InvalidOrExpired, statusCode, detail);
    }

    private static string? ExtractErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var error = JObject.Parse(body)["error"]?.Value<string>()?.Trim();
            return string.IsNullOrWhiteSpace(error) ? null : error;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }
}
