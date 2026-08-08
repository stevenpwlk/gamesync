using System.Net.Http.Json;
using GameSaveHub.Contracts;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Service;

public sealed class ServerEnrollmentClient(HttpClient http, IOptions<ClientServiceOptions> options, DeviceIdentity identity, ClientStateStore stateStore)
{
    private readonly Uri _baseUri = new(options.Value.ServerBaseUrl, UriKind.Absolute);

    public async Task<ClientPersistentState> EnrollAsync(string code, string deviceName, string playerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        var state = await stateStore.ReadAsync(cancellationToken);
        if (state.DeviceId is not null) throw new InvalidOperationException("Cet appareil est déjà enrôlé.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "api/v1/enrollments/redeem"))
        {
            Content = JsonContent.Create(new EnrollmentRedeemRequest(code, deviceName, identity.GetOrCreatePublicKeyPem()))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: cancellationToken);
            throw new InvalidOperationException(error?.Code ?? $"HTTP {(int)response.StatusCode}");
        }
        var enrollment = await response.Content.ReadFromJsonAsync<EnrollmentRedeemResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Réponse d'enrôlement vide.");
        var updated = state with { SchemaVersion = 2, DeviceId = enrollment.DeviceId, DeviceName = deviceName, EnrolledAtUtc = DateTimeOffset.UtcNow, RegisteredPlayerName = playerName.Trim() };
        await stateStore.WriteAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<bool> IsServerHealthyAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(_baseUri, "healthz"), cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
