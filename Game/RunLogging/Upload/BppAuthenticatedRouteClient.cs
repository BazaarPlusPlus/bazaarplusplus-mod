#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal interface IBppAuthenticatedApiResult
{
    bool Succeeded { get; }

    string? Error { get; }

    bool ShouldFallback { get; }

    bool ShouldReRegister { get; }
}

internal readonly struct BppAuthenticatedRequestResult<TResult>
    where TResult : struct, IBppAuthenticatedApiResult
{
    private BppAuthenticatedRequestResult(bool registrationAvailable, TResult response)
    {
        RegistrationAvailable = registrationAvailable;
        Response = response;
    }

    public bool RegistrationAvailable { get; }

    public TResult Response { get; }

    public static BppAuthenticatedRequestResult<TResult> RegistrationUnavailable() =>
        new(false, default);

    public static BppAuthenticatedRequestResult<TResult> Success(TResult response) =>
        new(true, response);
}

internal sealed class BppAuthenticatedRouteClient
{
    private readonly RunUploadRegistrationClient _registrationClient;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly string _clientStateScope;

    public BppAuthenticatedRouteClient(
        RunUploadRegistrationClient registrationClient,
        RunUploadClientStateStore clientStateStore,
        string clientStateScope
    )
    {
        _registrationClient =
            registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        if (string.IsNullOrWhiteSpace(clientStateScope))
            throw new ArgumentException(
                "Client state scope is required.",
                nameof(clientStateScope)
            );

        _clientStateScope = clientStateScope.Trim();
    }

    public async Task<BppAuthenticatedRequestResult<TResult>> SendAsync<TResult>(
        string installId,
        Func<string, CancellationToken, Task<TResult>> sendAsync,
        CancellationToken cancellationToken
    )
        where TResult : struct, IBppAuthenticatedApiResult
    {
        if (sendAsync == null)
            throw new ArgumentNullException(nameof(sendAsync));

        var clientId = await _registrationClient.EnsureClientRegistrationAsync(
            installId,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(clientId))
            return BppAuthenticatedRequestResult<TResult>.RegistrationUnavailable();

        var response = await sendAsync(clientId, cancellationToken);
        if (!response.Succeeded && response.ShouldReRegister)
        {
            _clientStateStore.ClearScopedClientId(_clientStateScope);
            clientId = await _registrationClient.EnsureClientRegistrationAsync(
                installId,
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(clientId))
                return BppAuthenticatedRequestResult<TResult>.RegistrationUnavailable();

            response = await sendAsync(clientId, cancellationToken);
        }

        return BppAuthenticatedRequestResult<TResult>.Success(response);
    }
}
