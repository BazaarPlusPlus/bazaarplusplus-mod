#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.Identity;

namespace BazaarPlusPlus.Game.Online;

internal sealed class InstallationRequestSigner
{
    private readonly InstallationRecordStore _installationStore;

    public InstallationRequestSigner(InstallationRecordStore installationStore)
    {
        _installationStore =
            installationStore ?? throw new ArgumentNullException(nameof(installationStore));
    }

    public HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string endpoint,
        byte[]? bodyBytes,
        InstallationRecord installation,
        string timestamp,
        string? contentType = null
    )
    {
        if (method == null)
            throw new ArgumentNullException(nameof(method));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        if (installation == null)
            throw new ArgumentNullException(nameof(installation));
        if (string.IsNullOrWhiteSpace(timestamp))
            throw new ArgumentException("Timestamp is required.", nameof(timestamp));

        var requestUri = new Uri(endpoint);
        var normalizedQuery = NormalizeQueryString(requestUri.Query);
        var bodyHash = ComputeBodyHash(bodyBytes);
        var canonical = BuildCanonicalRequest(
            method.Method,
            requestUri.AbsolutePath,
            normalizedQuery,
            installation.InstallationId,
            timestamp,
            bodyHash
        );

        using var rsa = LoadPrivateKey();
        var signature = Convert.ToBase64String(
            rsa.SignData(
                Encoding.UTF8.GetBytes(canonical),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            )
        );

        var request = new HttpRequestMessage(method, requestUri);
        if (bodyBytes != null)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new(contentType ?? "application/json");
        }

        request.Headers.TryAddWithoutValidation(
            "X-BPP-Installation-Id",
            installation.InstallationId
        );
        request.Headers.TryAddWithoutValidation("X-BPP-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-BPP-Content-SHA256", bodyHash);
        request.Headers.TryAddWithoutValidation("X-BPP-Signature", signature);
        return request;
    }

    internal static string BuildCanonicalRequest(
        string method,
        string absolutePath,
        string normalizedQuery,
        string installationId,
        string timestamp,
        string bodyHash
    )
    {
        return string.Join(
            "\n",
            new[]
            {
                method.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(absolutePath) ? "/" : absolutePath.Trim(),
                normalizedQuery.Trim(),
                installationId.Trim(),
                timestamp.Trim(),
                bodyHash.Trim(),
            }
        );
    }

    internal static string NormalizeQueryString(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query[0] == '?' ? query[1..] : query;
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        return string.Join(
            "&",
            trimmed
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment =>
                {
                    var parts = segment.Split('=', 2);
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                    var encodedKey = Uri.EscapeDataString(key);
                    var encodedValue = Uri.EscapeDataString(value);
                    return new KeyValuePair<string, string>(encodedKey, encodedValue);
                })
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ThenBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}")
        );
    }

    internal static string ComputeBodyHash(byte[]? bodyBytes)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(bodyBytes ?? Array.Empty<byte>()));
    }

    private RSA LoadPrivateKey()
    {
        if (_installationStore.TryLoadPrivateKey(out var rsa))
            return rsa;

        throw new InvalidOperationException("Installation private key is unavailable.");
    }
}
