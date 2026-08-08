using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GameSaveHub.Contracts;

if (args.Length is not 3 and not 4)
{
    Console.Error.WriteLine("Usage: GameSaveHub.ApiSmoke <base-url> <enrollment-code> <world-id> [artifact.gshsave]");
    return 2;
}

using var http = new HttpClient { BaseAddress = new Uri(args[0]), Timeout = TimeSpan.FromSeconds(10) };
using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

using var health = await http.GetAsync("/healthz");
health.EnsureSuccessStatusCode();

var redeem = await PostAsync<EnrollmentRedeemRequest, EnrollmentRedeemResponse>(
    "/api/v1/enrollments/redeem",
    new(args[1], "Smoke-PC", ecdsa.ExportSubjectPublicKeyInfoPem()));
var challenge = await PostAsync<AuthChallengeRequest, AuthChallengeResponse>(
    "/api/v1/auth/challenges",
    new(redeem.DeviceId));
var signature = ecdsa.SignData(Convert.FromBase64String(challenge.Nonce), HashAlgorithmName.SHA256);
var token = await PostAsync<AuthTokenRequest, AuthTokenResponse>(
    "/api/v1/auth/tokens",
    new(redeem.DeviceId, challenge.ChallengeId, Convert.ToBase64String(signature)));

http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
var worldId = Guid.Parse(args[2]);
var status = await http.GetFromJsonAsync<WorldStatusResponse>($"/api/v1/worlds/{worldId:D}/status")
    ?? throw new InvalidOperationException("Réponse world status vide.");

using var acquisition = NewMutation(HttpMethod.Post, $"/api/v1/worlds/{worldId:D}/acquire");
acquisition.Content = JsonContent.Create(new AcquireWorldRequest(status.CurrentVersionId));
using var acquisitionResponse = await http.SendAsync(acquisition);
if (args.Length == 3)
{
    var gate = await acquisitionResponse.Content.ReadFromJsonAsync<ApiError>();
    if (acquisitionResponse.StatusCode != HttpStatusCode.Conflict || gate?.Code != "host_transfer_not_validated")
        throw new InvalidOperationException($"Feature gate inattendu : {(int)acquisitionResponse.StatusCode} {gate?.Code}.");

    Console.WriteLine($"Health={(int)health.StatusCode}; Enrollment=OK; SignedChallenge=OK; TokenMinutes={(token.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes:F1}; World={status.Status}; Gate={gate.Code}");
    return 0;
}

acquisitionResponse.EnsureSuccessStatusCode();
var acquired = await acquisitionResponse.Content.ReadFromJsonAsync<AcquireWorldResponse>()
    ?? throw new InvalidOperationException("Réponse d'acquisition vide.");
await PostNoContentAsync(
    $"/api/v1/sessions/{acquired.SessionId:D}/report-failure",
    JsonContent.Create(new ReportFailureRequest("smoke_preimport_interruption", "Validation de la reprise avant import.")));
await PostNoContentAsync($"/api/v1/sessions/{acquired.SessionId:D}/import-starting", content: null);

var artifactPath = Path.GetFullPath(args[3]);
var artifactLength = new FileInfo(artifactPath).Length;
await using var hashStream = File.OpenRead(artifactPath);
var artifactHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(hashStream));
var upload = await PostAsync<CreateUploadRequest, CreateUploadResponse>(
    $"/api/v1/sessions/{acquired.SessionId:D}/uploads",
    new(artifactLength, artifactHash, status.CurrentVersionId, 4 * 1024 * 1024));

using (var chunk = NewMutation(HttpMethod.Put, $"/api/v1/uploads/{upload.UploadId:D}/chunks/0"))
{
    chunk.Content = new StreamContent(File.OpenRead(artifactPath));
    chunk.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    using var chunkResponse = await http.SendAsync(chunk);
    chunkResponse.EnsureSuccessStatusCode();
}

await PostNoContentAsync(
    $"/api/v1/sessions/{acquired.SessionId:D}/report-failure",
    JsonContent.Create(new ReportFailureRequest("smoke_interruption", "Validation de la reprise d'upload.")));
var resumedUpload = await PostAsync<CreateUploadRequest, CreateUploadResponse>(
    $"/api/v1/sessions/{acquired.SessionId:D}/uploads",
    new(artifactLength, artifactHash, status.CurrentVersionId, 4 * 1024 * 1024));
if (resumedUpload.UploadId != upload.UploadId || !resumedUpload.ReceivedChunks.Contains(0))
    throw new InvalidOperationException("La reprise d'upload n'a pas retrouvé le manifeste et le chunk déjà confirmé.");

var committed = await PostAsync<object, CommitUploadResponse>($"/api/v1/uploads/{upload.UploadId:D}/commit", new { });
var repeatedCommit = await PostAsync<object, CommitUploadResponse>($"/api/v1/uploads/{upload.UploadId:D}/commit", new { });
if (repeatedCommit.VersionId != committed.VersionId) throw new InvalidOperationException("Le commit répété n'est pas idempotent.");
var finalStatus = await http.GetFromJsonAsync<WorldStatusResponse>($"/api/v1/worlds/{worldId:D}/status")
    ?? throw new InvalidOperationException("Réponse finale vide.");
if (finalStatus.Status != "Available" || finalStatus.CurrentVersionId != committed.VersionId)
    throw new InvalidOperationException("Le monde n'est pas revenu à Available sur la version publiée.");

Console.WriteLine($"Health={(int)health.StatusCode}; Enrollment=OK; SignedChallenge=OK; AcquireResume=OK; UploadResume=OK; Commit={committed.VersionId:D}; CommitReplay=OK; World={finalStatus.Status}");
return 0;

async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body)
{
    using var request = NewMutation(HttpMethod.Post, path);
    request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TResponse>() ?? throw new InvalidOperationException($"Réponse vide : {path}.");
}

async Task PostNoContentAsync(string path, HttpContent? content)
{
    using var request = NewMutation(HttpMethod.Post, path);
    request.Content = content;
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
}

static HttpRequestMessage NewMutation(HttpMethod method, string path)
{
    var request = new HttpRequestMessage(method, path);
    request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
    return request;
}
