#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.BuildRecommendations;

/// <summary>
/// Shared manual-refresh entry over the ten-win build corpus. Wraps the repository's synchronous
/// remote refresh in a worker task so panel callers can await it off the Unity main thread. The
/// token only prevents the task from starting (the underlying HTTP read is synchronous and not
/// interruptible once issued); callers guard their own stale continuations.
/// </summary>
internal sealed class BuildRecommendationRefreshService
{
    public async Task<BuildRecommendationRefreshResult> RefreshAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await Task.Run(
                    () =>
                    {
                        var succeeded =
                            BuildRecommendationRepository.TryRefreshFinalBuildsFromRemote(
                                out var error
                            );
                        return succeeded
                            ? BuildRecommendationRefreshResult.Success()
                            : BuildRecommendationRefreshResult.Failure(error);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
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
