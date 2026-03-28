#nullable enable
using System;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private GhostBattleSyncService? TryCreateGhostSyncService(HistoryPanelRepository? repository)
    {
        if (repository == null || BppRuntimeHost.Config.EnableRunUploadConfig?.Value != true)
            return null;

        var uploadEndpoint = BppRuntimeHost.Config.RunUploadEndpointConfig?.Value?.Trim();
        var registrationEndpoint =
            BppRuntimeHost.Config.RunUploadRegistrationEndpointConfig?.Value?.Trim();
        var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
        var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
        var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;

        if (
            string.IsNullOrWhiteSpace(identityPath)
            || string.IsNullOrWhiteSpace(clientStatePath)
            || string.IsNullOrWhiteSpace(privateKeyPath)
        )
        {
            return null;
        }

        var endpoint = TryBuildEndpointSet(registrationEndpoint, uploadEndpoint);
        if (endpoint == null)
            return null;

        return new GhostBattleSyncService(
            repository,
            new RunUploadIdentityStore(identityPath),
            new RunUploadClientStateStore(clientStatePath),
            new RunUploadKeyStore(privateKeyPath),
            endpoint,
            timeout: TimeSpan.FromSeconds(10)
        );
    }

    private static RunUploadEndpointSet? TryBuildEndpointSet(
        string? registrationEndpoint,
        string? uploadEndpoint
    )
    {
        if (
            string.IsNullOrWhiteSpace(registrationEndpoint)
            || string.IsNullOrWhiteSpace(uploadEndpoint)
        )
        {
            return null;
        }

        if (
            !Uri.TryCreate(registrationEndpoint, UriKind.Absolute, out var registrationUri)
            || !Uri.TryCreate(uploadEndpoint, UriKind.Absolute, out var uploadUri)
            || !IsSupportedScheme(registrationUri)
            || !IsSupportedScheme(uploadUri)
        )
        {
            return null;
        }

        return new RunUploadEndpointSet
        {
            RegistrationEndpoint = registrationUri.ToString(),
            UploadEndpoint = uploadUri.ToString(),
        };
    }

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
