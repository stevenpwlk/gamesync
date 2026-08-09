using System.Text.Json;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ApiContractCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OldWorldStatusJsonRemainsReadable()
    {
        var worldId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var json = $$"""
            {"worldId":"{{worldId}}","name":"Monde principal","status":"Available","currentVersionId":"{{versionId}}","activeSessionId":null}
            """;

        var status = JsonSerializer.Deserialize<WorldStatusResponse>(json, JsonOptions);

        Assert.NotNull(status);
        Assert.Equal(worldId, status.WorldId);
        Assert.Null(status.ActiveSession);
        Assert.Null(status.LastActivity);
    }

    [Fact]
    public void PresenceFieldsRoundTripAdditively()
    {
        var now = new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero);
        var status = new WorldStatusResponse(
            Guid.NewGuid(),
            "Monde principal",
            "InGame",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ActiveWorldSessionResponse(Guid.NewGuid(), Guid.NewGuid(), "Bob", "InGame", now, now.AddMinutes(2)),
            new WorldLastActivityResponse(Guid.NewGuid(), "Steven", now.AddHours(-1)));

        var json = JsonSerializer.Serialize(status, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<WorldStatusResponse>(json, JsonOptions);

        Assert.Equal("Bob", roundTrip!.ActiveSession!.PlayerName);
        Assert.Equal("Steven", roundTrip.LastActivity!.PlayerName);
    }

    [Fact]
    public void OldAcquireJsonLeavesPlayerNameAbsent()
    {
        var versionId = Guid.NewGuid();

        var request = JsonSerializer.Deserialize<AcquireWorldRequest>(
            $"{{\"expectedVersionId\":\"{versionId}\"}}",
            JsonOptions);

        Assert.Equal(versionId, request!.ExpectedVersionId);
        Assert.Null(request.PlayerName);
    }

    [Theory]
    [InlineData(HostPreparationOutcome.Prepared, 0)]
    [InlineData(HostPreparationOutcome.AlreadyHost, 1)]
    [InlineData(HostPreparationOutcome.PlayerNotFound, 2)]
    [InlineData(HostPreparationOutcome.PlayerAmbiguous, 3)]
    [InlineData(HostPreparationOutcome.InvalidArtifact, 4)]
    [InlineData(HostPreparationOutcome.InvalidPlayerTopology, 5)]
    [InlineData(HostPreparationOutcome.Failed, 6)]
    public void HostPreparationOutcomePreservesPublishedNumericValues(
        HostPreparationOutcome outcome,
        int expectedValue)
    {
        Assert.Equal(expectedValue, (int)outcome);
    }
}
