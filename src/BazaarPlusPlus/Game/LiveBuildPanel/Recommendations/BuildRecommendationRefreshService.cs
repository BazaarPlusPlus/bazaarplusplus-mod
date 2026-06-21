#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Shared manual-refresh entry over the ten-win build corpus. Awaits the repository's async
/// remote refresh so panel callers stay off the Unity main thread while the download is in
/// flight. The token only prevents the refresh from starting (the HTTP read is not interruptible
/// once issued); callers guard their own stale continuations.
/// </summary>
internal sealed class BuildRecommendationRefreshService
{
    // A manual pull really hits the server only once per session: the ten-win corpus regenerates
    // only every few hours server-side, so any later pull in the same session reports a synthetic
    // success (a no-op "fake update") instead of re-downloading. A failed pull does not consume the
    // allowance, so the user can retry.
    private bool _hasSuccessfullyPulled;

    public async Task<BuildRecommendationRefreshResult> RefreshAsync(
        BuildRecommendationRepository repository,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_hasSuccessfullyPulled)
        {
            BppLog.Info(
                "BuildRecommendationRefreshService",
                "Ten-win builds already pulled this session; returning synthetic success."
            );
            return BuildRecommendationRefreshResult.Success();
        }

        try
        {
            var (succeeded, error) = await repository
                .TryRefreshFinalBuildsFromRemoteAsync()
                .ConfigureAwait(false);
            if (succeeded)
                _hasSuccessfullyPulled = true;
            return succeeded
                ? BuildRecommendationRefreshResult.Success()
                : BuildRecommendationRefreshResult.Failure(error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildRecommendationRefreshResult.Failure(ex.Message);
        }
    }
}

internal readonly struct BuildRecommendationRefreshResult
{
    private BuildRecommendationRefreshResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static BuildRecommendationRefreshResult Success() => new(true, null);

    public static BuildRecommendationRefreshResult Failure(string? error) => new(false, error);
}
