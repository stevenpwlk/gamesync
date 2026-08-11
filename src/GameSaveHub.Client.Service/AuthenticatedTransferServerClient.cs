using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Service;

/// <summary>Issue d'une demande de révocation d'appareil, du point de vue du poste.</summary>
public enum RevokeSelfOutcome
{
    /// <summary>Le serveur a bien révoqué cet appareil (ou il n'y avait rien à révoquer).</summary>
    Revoked,

    /// <summary>Le serveur a refusé : une session est encore active sur cet appareil (409).</summary>
    ActiveSessionBlocked,

    /// <summary>Le serveur n'a pas pu être joint ou a répondu une autre erreur.</summary>
    Unreachable
}

public sealed class AuthenticatedTransferServerClient(
    HttpClient http,
    IOptions<ClientServiceOptions> options,
    DeviceIdentity identity,
    ClientStateStore stateStore) : ITransferServerClient, IDisposable
{
    public const string ClientVersionHeaderName = "X-GameSaveHub-Client-Version";
    public const string ClientVersion = "0.4.0";

    private readonly Uri _baseUri = new(options.Value.ServerBaseUrl, UriKind.Absolute);
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;

    public async Task<IReadOnlyList<WorldCatalogItemResponse>> ListWorldsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api("worlds"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadRequiredAsync<WorldCatalogItemResponse[]>(response, cancellationToken);
    }

    public async Task<WorldPreviewResponse> GetWorldPreviewAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api($"worlds/{worldId:D}/preview"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadRequiredAsync<WorldPreviewResponse>(response, cancellationToken);
    }

    public async Task<WorldStatusResponse> GetWorldStatusAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api($"worlds/{worldId:D}/status"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadRequiredAsync<WorldStatusResponse>(response, cancellationToken);
    }

    public async Task<AcquireWorldResponse> AcquireWorldAsync(
        Guid worldId,
        Guid? expectedVersionId,
        string playerName,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"worlds/{worldId:D}/acquire"))
        {
            Content = JsonContent.Create(new AcquireWorldRequest(expectedVersionId, playerName))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadRequiredAsync<AcquireWorldResponse>(response, cancellationToken);
    }

    public async Task<ServerArtifactDownload> DownloadSessionArtifactAsync(
        Guid serverSessionId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api($"sessions/{serverSessionId:D}/artifact"));
        using var response = await SendAuthorizedAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NoContent) return new ServerArtifactDownload(false, null, null, null);
        await EnsureSuccessAsync(response, cancellationToken);

        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination) ?? throw new InvalidOperationException("Dossier de téléchargement invalide."));
        var temporary = fullDestination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(true);
            }
            var info = new FileInfo(temporary);
            if (info.Length <= 0) throw new InvalidDataException("Artefact serveur vide.");
            var sha256 = await ComputeSha256Async(temporary, cancellationToken);
            File.Move(temporary, fullDestination, overwrite: true);
            return new ServerArtifactDownload(true, fullDestination, sha256, info.Length);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public async Task MarkImportStartingAsync(Guid serverSessionId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"sessions/{serverSessionId:D}/import-starting"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task HeartbeatAsync(Guid serverSessionId, string clientState, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"sessions/{serverSessionId:D}/heartbeat"))
        {
            Content = JsonContent.Create(new SessionHeartbeatRequest(clientState))
        };
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<CreateUploadResponse> CreateUploadAsync(
        Guid serverSessionId,
        CreateUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Api($"sessions/{serverSessionId:D}/uploads"))
        {
            Content = JsonContent.Create(request)
        };
        using var response = await SendAuthorizedAsync(httpRequest, cancellationToken);
        return await ReadRequiredAsync<CreateUploadResponse>(response, cancellationToken);
    }

    public async Task PutUploadChunkAsync(
        Guid uploadId,
        int index,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Api($"uploads/{uploadId:D}/chunks/{index}"))
        {
            Content = new ByteArrayContent(content.ToArray())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<CommitUploadResponse> CommitUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"uploads/{uploadId:D}/commit"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        return await ReadRequiredAsync<CommitUploadResponse>(response, cancellationToken);
    }

    public async Task AbortSessionAsync(Guid serverSessionId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"sessions/{serverSessionId:D}/abort"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReportFailureAsync(Guid serverSessionId, string code, string message, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api($"sessions/{serverSessionId:D}/report-failure"))
        {
            Content = JsonContent.Create(new ReportFailureRequest(code, message))
        };
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Révoque cet appareil côté serveur avant une désinstallation. Ne lève jamais, mais
    /// distingue les trois issues, qui appellent trois conduites différentes côté
    /// désinstallation. Le <c>409 device_has_active_session</c> existe précisément pour
    /// qu'une révocation n'orpheline jamais une écriture en cours (§7 de la spécification) :
    /// le confondre avec « serveur injoignable », comme le faisait le <c>bool</c> initial,
    /// revenait à jeter ce signal et à désinstaller quand même.
    /// </summary>
    public async Task<RevokeSelfOutcome> RevokeSelfAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Api("device/revoke-self"));
            using var response = await SendAuthorizedAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return RevokeSelfOutcome.Revoked;
            return response.StatusCode == HttpStatusCode.Conflict
                ? RevokeSelfOutcome.ActiveSessionBlocked
                : RevokeSelfOutcome.Unreachable;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or TransferServerException)
        {
            return RevokeSelfOutcome.Unreachable;
        }
    }

    private Uri Api(string relative) => new(_baseUri, "api/v1/" + relative);

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        EnsureMutationIdempotency(request);
        request.Headers.Add(ClientVersionHeaderName, ClientVersion);
        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, completionOption, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_accessToken is not null && _accessTokenExpiresAtUtc > now.AddMinutes(1)) return _accessToken;

            var state = await stateStore.ReadAsync(cancellationToken);
            var deviceId = state.DeviceId ?? throw new TransferServerException("device_not_enrolled", "Ce PC n'est pas associé au serveur GameSave Hub.");
            using var challengeRequest = new HttpRequestMessage(HttpMethod.Post, Api("auth/challenges"))
            {
                Content = JsonContent.Create(new AuthChallengeRequest(deviceId))
            };
            EnsureMutationIdempotency(challengeRequest);
            using var challengeResponse = await http.SendAsync(challengeRequest, cancellationToken);
            var challenge = await ReadRequiredAsync<AuthChallengeResponse>(challengeResponse, cancellationToken);
            var nonce = Convert.FromBase64String(challenge.Nonce);
            var signature = Convert.ToBase64String(identity.Sign(nonce));

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, Api("auth/tokens"))
            {
                Content = JsonContent.Create(new AuthTokenRequest(deviceId, challenge.ChallengeId, signature))
            };
            EnsureMutationIdempotency(tokenRequest);
            using var tokenResponse = await http.SendAsync(tokenRequest, cancellationToken);
            var token = await ReadRequiredAsync<AuthTokenResponse>(tokenResponse, cancellationToken);
            _accessToken = token.AccessToken;
            _accessTokenExpiresAtUtc = token.ExpiresAtUtc;
            return token.AccessToken;
        }
        catch (FormatException exception)
        {
            throw new TransferServerException("auth_challenge_invalid", exception.Message);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new TransferServerException("empty_response", "Le serveur a renvoyé une réponse vide.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            // Réponse non JSON : le code HTTP sera utilisé.
        }
        throw new TransferServerException(error?.Code ?? $"http_{(int)response.StatusCode}", error?.Message ?? $"Erreur serveur HTTP {(int)response.StatusCode}.");
    }

    private static void EnsureMutationIdempotency(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post && request.Method != HttpMethod.Put) return;
        if (request.Headers.Contains("Idempotency-Key")) return;
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public void Dispose() => _tokenGate.Dispose();
}
