using System.IO.Pipes;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace GameSaveHub.Client.App;

public sealed record PipeRequest(
    string Command,
    string? EnrollmentCode = null,
    string? DeviceName = null,
    Guid? WorldId = null,
    string? PlayerName = null,
    Guid? TransferSessionId = null);
public sealed record PipeResponse(bool Success, string Code, string Message, JsonElement? Data);

public sealed class PipeClient(string pipeName = "GameSaveHub.Client")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken cancellationToken = default)
    {
        // Identification, et pas Impersonation : le service doit pouvoir vérifier QUI
        // se connecte, sans jamais pouvoir agir sous cette identité. C'est le niveau
        // minimal permettant le contrôle du SID côté serveur.
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(3000, cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory(), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken);
        return line is null
            ? new PipeResponse(false, "empty_response", "Le service n'a pas répondu.", null)
            : JsonSerializer.Deserialize<PipeResponse>(line, JsonOptions) ?? new(false, "invalid_response", "Réponse locale invalide.", null);
    }
}
