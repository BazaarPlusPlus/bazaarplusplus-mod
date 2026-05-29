#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class RunBundleClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public RunBundleClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<RunBundleUploadResult> UploadRunBundleAsync(
        RunBundleUploadRequest payload,
        CancellationToken cancellationToken
    )
    {
        var result = await ModApiJsonPost.PostJsonAsync(
            _httpClient,
            _routes.UploadRunBundle,
            payload,
            cancellationToken
        );
        if (result.IsSuccess)
            return RunBundleUploadResult.Success();

        return RunBundleUploadResult.Failure(
            ModApiErrorFormatter.FormatHttpFailure(result.StatusCode, result.FailureBody!)
        );
    }
}

public readonly struct RunBundleUploadResult
{
    private RunBundleUploadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static RunBundleUploadResult Success() => new(true, null);

    public static RunBundleUploadResult Failure(string error) => new(false, error);
}
